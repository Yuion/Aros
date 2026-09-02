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

public record VocabAnswerResult(bool Correct, string Expected, string Characters, string? Note);

public class VocabException(string message) : Exception(message);

public class VocabService(AppDbContext db, IMemoryCache cache)
{
    public const int DefaultQuestionCount = 10;
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
    }

    public async Task<VocabSession> BuildSessionAsync(
        int questionCount, VocabDirection? only, string? tag, CancellationToken ct)
    {
        var words = await db.VocabWords
            .Include(w => w.Progress)
            .Where(w => !w.NeedsReview)          // a guessed reading must never be drilled in
            .AsNoTracking()
            .ToListAsync(ct);

        if (tag is { Length: > 0 })
            words = words.Where(w => w.Tags.Contains(tag)).ToList();

        var unique = PromptCounts(words);

        var testable = words
            .Select(word => new
            {
                Word = word,
                Directions = Directions(word, unique).Where(d => only is null || d == only).ToList(),
            })
            .Where(x => x.Directions.Count > 0)
            .ToList();

        if (testable.Count == 0)
            throw new VocabException(
                words.Count == 0
                    ? "No vocabulary yet. Add sentences in Chinese TTS and the words will be collected from them."
                    : "No words are testable in that direction yet — they may still need review.");

        // One question per word, so a ten-question round covers ten words
        var wanted = Math.Clamp(questionCount, 1, testable.Count);
        var chosen = DrawWeight.PickWithoutReplacement(
            testable, wanted, x => x.Directions.Max(d => Weight(x.Word, d)));

        var questions = new List<VocabQuestion>(chosen.Count);

        foreach (var pick in chosen)
        {
            // Within the word, favour the direction it is weakest in
            var direction = DrawWeight
                .PickWithoutReplacement(pick.Directions, 1, d => Weight(pick.Word, d))
                .Single();

            questions.Add(BuildQuestion(pick.Word, direction, words));
        }

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
        var progress = word.Progress.FirstOrDefault(p => p.Direction == direction);

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

    private static double Weight(VocabWord word, VocabDirection direction) =>
        word.Progress.FirstOrDefault(p => p.Direction == direction) is { } progress
            ? DrawWeight.For(progress.WrongCount, progress.ConsecutiveCorrect, progress.LastSeenAt)
            : DrawWeight.Unseen;

    private QuestionState Lookup(Guid token) =>
        cache.TryGetValue(CacheKey(token), out QuestionState? state) && state is not null
            ? state
            : throw new VocabException("This round has expired. Start a new one.");

    private static string CacheKey(Guid token) => $"vocab:{token}";
}
