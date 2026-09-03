using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Listening;
using Aros.Api.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aros.Api.Vocab;

public record VocabOption(int WordId, string Characters);

public record VocabQuestion(
    Guid Token,
    VocabDirection Direction,
    string Prompt,
    string PromptLabel,
    string AnswerLabel,
    bool Typed,
    IReadOnlyList<VocabOption>? Options);

public record VocabSession(IReadOnlyList<VocabQuestion> Questions);

public record VocabAnswerResult(bool Correct, string Expected, string Characters, string? Note, bool Retry = false);

public class VocabException(string message) : Exception(message);

public class VocabService(AppDbContext db, IMemoryCache cache)
{
    /// <summary>Questions per direction in a full round — six directions, so eighteen questions.</summary>
    public const int DefaultPerDirection = 3;

    /// <summary>Length of a round when one direction is being drilled on its own.</summary>
    private const int SingleDirectionCount = 10;

    private const int OptionCount = 3;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(2);

    /// <summary>The two directions that ask for characters are multiple choice — typing them needs an IME.</summary>
    private static bool IsTyped(VocabDirection direction) =>
        direction is not (VocabDirection.PinyinToCharacters or VocabDirection.EnglishToCharacters);

    private sealed class QuestionState
    {
        public required int WordId { get; init; }
        public required VocabDirection Direction { get; init; }
        public bool Answered { get; set; }
        public bool Retried { get; set; }
    }

    /// <summary>
    /// A round is a block of questions per direction, the blocks in random order — so every
    /// direction gets equal practice and you settle into one kind of question at a time instead of
    /// being thrown between six. Picking a single direction turns the round into a drill of just
    /// that one, which is the only reason to filter.
    /// </summary>
    public async Task<VocabSession> BuildSessionAsync(
        int perDirection, VocabDirection? only, string? tag, CancellationToken ct)
    {
        var words = await db.VocabWords
            .Include(w => w.Progress)
            .Where(w => !w.NeedsReview)          // a guessed reading must never be drilled in
            .AsNoTracking()
            .ToListAsync(ct);

        if (tag is { Length: > 0 })
            words = words.Where(w => w.Tags.Contains(tag)).ToList();

        var unique = PromptCounts(words);

        // Which words can be asked in which direction. Rests and mastery are judged per
        // (word, direction), so mastering 水 → "water" leaves "water" → 水 in full rotation:
        // they are separate skills and separately scored, and one says nothing about the other.
        var testable = Enum.GetValues<VocabDirection>()
            .Where(direction => only is null || direction == only)
            .ToDictionary(
                direction => direction,
                direction => words.Where(w => Directions(w, unique).Contains(direction)).ToList());

        var candidates = testable.ToDictionary(pair => pair.Key, pair => Askable(pair.Value, pair.Key));

        if (candidates.Values.All(list => list.Count == 0))
            throw new VocabException(
                words.Count == 0
                    ? "No vocabulary yet. Add sentences in Chinese TTS, or add a word directly — either way it waits in review first."
                    : testable.Values.Any(list => list.Count > 0)
                        ? "Everything testable there is mastered. Add more vocabulary."
                        : "No words are testable in that direction yet — they may still be waiting for review.");

        var perBlock = only is null
            ? Math.Max(1, perDirection)
            : SingleDirectionCount;          // filtering means drilling that one direction

        var blocks = new List<List<VocabQuestion>>();

        foreach (var (direction, pool) in candidates)
        {
            if (pool.Count == 0) continue;

            var picked = DrawWeight.PickWithoutReplacement(
                pool, Math.Min(perBlock, pool.Count), word => Weight(word, direction));

            blocks.Add(picked.Select(word => BuildQuestion(word, direction, words)).ToList());
        }

        // Which direction opens and which closes is left to chance
        var questions = blocks
            .OrderBy(_ => Random.Shared.Next())
            .SelectMany(block => block)
            .ToList();

        return new VocabSession(questions);
    }

    public async Task<VocabAnswerResult> AnswerAsync(
        Guid token, string? text, int? selectedWordId, CancellationToken ct)
    {
        var state = Lookup(token);

        var word = await db.VocabWords
            .Include(w => w.Progress)
            .FirstOrDefaultAsync(w => w.Id == state.WordId, ct)
            ?? throw new VocabException("That word no longer exists.");

        var expected = Answer(word, state.Direction);
        var (correct, note) = Judge(word, state.Direction, expected, text, selectedWordId);

        // Answering the question the round asked a moment ago rather than the one on screen is a
        // slip of attention, not a gap in knowledge. It gets one free retry, and nothing about
        // the real answer is given away with it.
        if (!correct && !state.Answered && !state.Retried
            && WrongDirection(word, state.Direction, text) is { } gave)
        {
            state.Retried = true;
            return new VocabAnswerResult(false, "", "", $"Wrong direction — that's the {gave}.", Retry: true);
        }

        if (!state.Answered)
        {
            state.Answered = true;
            RecordScore(word, state.Direction, correct);
            db.VocabAnswers.Add(new VocabAnswer
            {
                VocabWordId = word.Id,
                Direction = state.Direction,
                Correct = correct,
            });
            await db.SaveChangesAsync(ct);
        }

        return new VocabAnswerResult(correct, expected, word.Characters, note);
    }

    private static (bool Correct, string? Note) Judge(
        VocabWord word, VocabDirection direction, string expected, string? text, int? selectedWordId)
    {
        if (!IsTyped(direction))
            return (selectedWordId == word.Id, null);

        var given = text ?? "";

        return direction switch
        {
            VocabDirection.CharactersToPinyin or VocabDirection.EnglishToPinyin =>
                AnswerCheck.PinyinMatches(expected, given)
                    ? (true, null)
                    : (false, AnswerCheck.IsToneOnlyMistake(expected, given)
                        ? "Right syllables, wrong tone."
                        : null),

            _ => (AnswerCheck.EnglishMatches(expected, given), null),
        };
    }

    /// <summary>
    /// Names the form the answer actually belongs to when it is right about this word but in the
    /// wrong one — 是 answered as "shi4" when the round asked for the English. Only forms other
    /// than the one being asked for count, and only for typed questions: a wrong multiple-choice
    /// tap is a wrong answer, not a misread prompt.
    /// </summary>
    private static string? WrongDirection(VocabWord word, VocabDirection direction, string? text)
    {
        if (!IsTyped(direction) || text is not { Length: > 0 }) return null;

        var asked = AnswerForm(direction);

        if (asked != "pinyin" && word.Pinyin.Length > 0 && AnswerCheck.PinyinMatches(word.Pinyin, text))
            return "pinyin";

        if (asked != "english" && word.English.Length > 0 && AnswerCheck.EnglishMatches(word.English, text))
            return "English";

        if (asked != "characters" && text.Trim() == word.Characters)
            return "characters";

        return null;
    }

    private VocabQuestion BuildQuestion(VocabWord word, VocabDirection direction, List<VocabWord> pool)
    {
        var token = Guid.NewGuid();
        cache.Set(
            CacheKey(token),
            new QuestionState { WordId = word.Id, Direction = direction },
            TokenLifetime);

        var options = IsTyped(direction) ? null : BuildOptions(word, pool);

        return new VocabQuestion(
            token,
            direction,
            Prompt(word, direction),
            Label(PromptForm(direction)),
            Label(AnswerForm(direction)),
            IsTyped(direction),
            options);
    }

    /// <summary>
    /// Wrong options are the closest words by single-character edits, reusing the listening
    /// trainer's rule so the choice turns on what actually differs. Options are deduplicated by
    /// characters — 行/xing2 beside 行/hang2 would look identical on screen and be unanswerable.
    /// </summary>
    private static List<VocabOption> BuildOptions(VocabWord word, List<VocabWord> pool)
    {
        var seen = new HashSet<string> { word.Characters };
        var distractors = new List<VocabWord>();

        var candidates = pool
            .Where(w => w.Id != word.Id)
            .OrderBy(w => SentenceSimilarity.Distance(word.Characters, w.Characters))
            .ThenBy(_ => Random.Shared.Next());

        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate.Characters)) continue;

            distractors.Add(candidate);
            if (distractors.Count == OptionCount - 1) break;
        }

        return distractors
            .Append(word)
            .OrderBy(_ => Random.Shared.Next())
            .Select(w => new VocabOption(w.Id, w.Characters))
            .ToList();
    }

    private sealed record PromptIndex(
        Dictionary<string, int> Characters,
        Dictionary<string, int> Pinyin,
        Dictionary<string, int> English);

    private static PromptIndex PromptCounts(List<VocabWord> words) => new(
        words.GroupBy(w => w.Characters).ToDictionary(g => g.Key, g => g.Count()),
        words.Where(w => w.Pinyin.Length > 0).GroupBy(w => w.Pinyin).ToDictionary(g => g.Key, g => g.Count()),
        words.Where(w => w.English.Length > 0).GroupBy(w => w.English).ToDictionary(g => g.Key, g => g.Count()));

    /// <summary>
    /// A direction is testable only when the answer form exists *and* the prompt picks out exactly
    /// one word. 他 and 她 are both ta1, so neither can be asked from pinyin — the prompt would have
    /// two right answers and marking either wrong would be a lie. Likewise a character with two
    /// readings cannot be asked for "its" pinyin. Ambiguity is dropped rather than guessed at, the
    /// same rule the listening trainer applies to sound-alikes.
    /// </summary>
    private static IEnumerable<VocabDirection> Directions(VocabWord word, PromptIndex unique)
    {
        var hasPinyin = word.Pinyin.Length > 0;
        var hasEnglish = word.English.Length > 0;

        var charactersIdentify = unique.Characters.GetValueOrDefault(word.Characters) == 1;
        var pinyinIdentifies = hasPinyin && unique.Pinyin.GetValueOrDefault(word.Pinyin) == 1;
        var englishIdentifies = hasEnglish && unique.English.GetValueOrDefault(word.English) == 1;

        if (charactersIdentify && hasPinyin) yield return VocabDirection.CharactersToPinyin;
        if (charactersIdentify && hasEnglish) yield return VocabDirection.CharactersToEnglish;
        if (pinyinIdentifies && hasEnglish) yield return VocabDirection.PinyinToEnglish;
        if (englishIdentifies && hasPinyin) yield return VocabDirection.EnglishToPinyin;
        if (pinyinIdentifies) yield return VocabDirection.PinyinToCharacters;
        if (englishIdentifies) yield return VocabDirection.EnglishToCharacters;
    }

    private static string Prompt(VocabWord word, VocabDirection direction) =>
        PromptForm(direction) switch
        {
            "characters" => word.Characters,
            "pinyin" => word.Pinyin,
            _ => word.English,
        };

    private static string Answer(VocabWord word, VocabDirection direction) =>
        AnswerForm(direction) switch
        {
            "characters" => word.Characters,
            "pinyin" => word.Pinyin,
            _ => word.English,
        };

    private static string PromptForm(VocabDirection direction) => direction switch
    {
        VocabDirection.CharactersToPinyin or VocabDirection.CharactersToEnglish => "characters",
        VocabDirection.PinyinToEnglish or VocabDirection.PinyinToCharacters => "pinyin",
        _ => "english",
    };

    private static string AnswerForm(VocabDirection direction) => direction switch
    {
        VocabDirection.CharactersToPinyin or VocabDirection.EnglishToPinyin => "pinyin",
        VocabDirection.CharactersToEnglish or VocabDirection.PinyinToEnglish => "english",
        _ => "characters",
    };

    private static string Label(string form) => form switch
    {
        "characters" => "Characters",
        "pinyin" => "Pinyin",
        _ => "English",
    };

    private static void RecordScore(VocabWord word, VocabDirection direction, bool correct)
    {
        var progress = Progress(word, direction);

        if (progress is null)
        {
            progress = new VocabProgress { VocabWordId = word.Id, Direction = direction };
            word.Progress.Add(progress);
        }

        if (correct)
        {
            progress.CorrectCount++;
            progress.ConsecutiveCorrect++;
        }
        else
        {
            progress.WrongCount++;
            progress.ConsecutiveCorrect = 0;
        }

        progress.LastSeenAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Drops words mastered in this direction for good, and resting ones until their rest is up.
    /// If a direction has nothing but resting words left, the rests are ignored rather than
    /// dropping the direction from the round — practising early beats not practising.
    /// </summary>
    private static List<VocabWord> Askable(List<VocabWord> words, VocabDirection direction)
    {
        var live = words.Where(w => Progress(w, direction) is not { } p || !DrawWeight.IsMastered(p.ConsecutiveCorrect)).ToList();
        var ready = live.Where(w => Progress(w, direction) is not { } p || !DrawWeight.IsResting(p.ConsecutiveCorrect, p.LastSeenAt)).ToList();

        return ready.Count > 0 ? ready : live;
    }

    private static VocabProgress? Progress(VocabWord word, VocabDirection direction) =>
        word.Progress.FirstOrDefault(p => p.Direction == direction);

    private static double Weight(VocabWord word, VocabDirection direction) =>
        Progress(word, direction) is { } progress
            ? DrawWeight.For(progress.WrongCount, progress.ConsecutiveCorrect, progress.LastSeenAt)
            : DrawWeight.Unseen;

    private QuestionState Lookup(Guid token) =>
        cache.TryGetValue(CacheKey(token), out QuestionState? state) && state is not null
            ? state
            : throw new VocabException("This round has expired. Start a new one.");

    private static string CacheKey(Guid token) => $"vocab:{token}";
}
