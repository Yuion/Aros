using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Listening;
using Aros.Api.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aros.Api.Vocab;

public record VocabQuestion(
    Guid Token,
    VocabDirection Direction,
    string Prompt,
    string PromptLabel,
    string AnswerLabel,
    bool Typed,
    IReadOnlyList<string>? Tiles);

public record VocabSession(IReadOnlyList<VocabQuestion> Questions);

public record VocabAnswerResult(
    bool Correct, int WordId, string Expected, string Characters, string? Note, bool Retry = false);

public class VocabException(string message) : Exception(message);

public class VocabService(AppDbContext db, IMemoryCache cache)
{
    /// <summary>Questions per direction in a full round — six directions, so eighteen questions.</summary>
    public const int DefaultPerDirection = 3;

    /// <summary>Length of a round when one direction is being drilled on its own.</summary>
    private const int SingleDirectionCount = 10;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// The two directions that ask for characters are answered from tiles rather than typed —
    /// writing them needs an IME. The answer still arrives as text, assembled from the tiles, so
    /// there is one way to answer a question and one way to judge it.
    /// </summary>
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
    ///
    /// A <paramref name="sweep"/> takes the whole pool instead of a sample: every word that is not
    /// resting, once each. The order is still drawn by weight, so the ones most due come up first.
    ///
    /// Even a sweep stops at <see cref="SessionBudget.Vocabulary"/> questions, and meets at most
    /// <see cref="SessionBudget.NewPerDay"/> words for the first time in a day. What does not fit
    /// is not lost: it stays due, and being the most overdue thing in the pool it is drawn first
    /// next time.
    /// </summary>
    public async Task<VocabSession> BuildSessionAsync(
        int perDirection, VocabDirection? only, string? tag, bool sweep, CancellationToken ct)
    {
        var words = await TestableAsync(tag, ct);
        var unique = PromptCounts(words);

        // Which words can be asked in which direction. Rests and mastery are judged per
        // (word, direction), so mastering 水 → "water" leaves "water" → 水 in full rotation:
        // they are separate skills and separately scored, and one says nothing about the other.
        var testable = Enum.GetValues<VocabDirection>()
            .Where(direction => only is null || direction == only)
            .ToDictionary(
                direction => direction,
                direction => words.Where(w => Directions(w, unique).Contains(direction)).ToList());

        var intake = SessionBudget.RemainingIntake(await IntroducedTodayAsync(ct));
        var recent = await RecentMissesAsync(ct);

        var candidates = testable.ToDictionary(
            pair => pair.Key,
            pair => SessionBudget.WithIntake(
                Askable(pair.Value, pair.Key), word => Progress(word, pair.Key) is null, intake));

        if (candidates.Values.All(list => list.Count == 0))
        {
            if (words.Count == 0)
                throw new VocabException(
                    "No vocabulary yet. Add sentences in Chinese TTS, or add a word directly — either way it waits in review first.");

            if (testable.Values.All(list => list.Count == 0))
                throw new VocabException(
                    "No words are testable in that direction yet — they may still be waiting for review.");

            // Nothing left to draw. Resting is temporary and worth dating; mastered is not.
            var tally = Tally(testable);
            throw new VocabException(
                tally.NextDueAt is { } due
                    ? $"Everything here is resting. The next word is due {Availability.Due(due)}."
                    : "Everything testable here is mastered. Add more vocabulary.");
        }

        // The budget is the sitting's, not each direction's, so it is split across the
        // directions that actually have something to ask
        var active = Math.Max(1, candidates.Count(pair => pair.Value.Count > 0));
        var share = Math.Max(1, SessionBudget.Vocabulary / active);

        var perBlock = sweep
            ? share
            : only is null
                ? Math.Min(Math.Max(1, perDirection), share)
                : Math.Min(SingleDirectionCount, SessionBudget.Vocabulary);

        var blocks = new List<List<VocabQuestion>>();

        foreach (var (direction, pool) in candidates)
        {
            if (pool.Count == 0) continue;

            var picked = DrawWeight.PickWorstFirst(
                pool, Math.Min(perBlock, pool.Count), word => Weight(word, direction, recent));

            blocks.Add(picked.Select(word => BuildQuestion(word, direction, words)).ToList());
        }

        // Which direction opens and which closes is left to chance
        var questions = blocks
            .OrderBy(_ => Random.Shared.Next())
            .SelectMany(block => block)
            .Take(SessionBudget.Vocabulary)     // rounding the share up must not exceed the budget
            .ToList();

        return new VocabSession(questions);
    }

    /// <summary>
    /// A fixed number of questions in one direction, for a session that decides its own shares —
    /// the mixed daily round, which weighs the directions against each other rather than giving
    /// each the same block.
    ///
    /// <paramref name="ignoreRests"/> is for practice past the day's work: nothing mastered comes
    /// back, but a word resting between rungs can be asked again. It changes nothing about scoring
    /// — a right answer is a right answer whenever it is given.
    /// </summary>
    public async Task<VocabSession> BuildSliceAsync(
        VocabDirection direction,
        int count,
        bool ignoreRests,
        IReadOnlySet<int>? exclude,
        CancellationToken ct)
    {
        if (count <= 0) return new VocabSession([]);

        var words = await TestableAsync(null, ct);
        var unique = PromptCounts(words);

        var pool = words
            .Where(w => Directions(w, unique).Contains(direction))
            .Where(w => exclude is null || !exclude.Contains(w.Id))
            .ToList();

        var askable = ignoreRests ? Unmastered(pool, direction) : Askable(pool, direction);

        if (!ignoreRests)
            askable = SessionBudget.WithIntake(
                askable,
                word => Progress(word, direction) is null,
                SessionBudget.RemainingIntake(await IntroducedTodayAsync(ct)));

        if (askable.Count == 0) return new VocabSession([]);

        var recent = await RecentMissesAsync(ct);

        var picked = DrawWeight.PickWorstFirst(
            askable, Math.Min(count, askable.Count), word => Weight(word, direction, recent));

        return new VocabSession([.. picked.Select(word => BuildQuestion(word, direction, words))]);
    }

    /// <summary>Everything still worth asking in a direction, rests set aside — mastery is not.</summary>
    private static List<VocabWord> Unmastered(List<VocabWord> words, VocabDirection direction) =>
        words.Where(w => w.RetiredAt is null
                         && (Progress(w, direction) is not { } p
                             || !Ladder(p).IsMastered(p.ConsecutiveCorrect)))
             .ToList();

    /// <summary>
    /// What a direction could ask, for a planner deciding how to spend the day. Ready is what it
    /// can ask *today*: the intake limit is part of the answer, or the planner would hand a share
    /// to a direction full of words it is not allowed to introduce yet.
    /// </summary>
    public async Task<(int Ready, int Unmastered)> StandingAsync(VocabDirection direction, CancellationToken ct)
    {
        var words = await TestableAsync(null, ct);
        var unique = PromptCounts(words);
        var pool = words.Where(w => Directions(w, unique).Contains(direction)).ToList();

        var ready = SessionBudget.WithIntake(
            Askable(pool, direction),
            word => Progress(word, direction) is null,
            SessionBudget.RemainingIntake(await IntroducedTodayAsync(ct)));

        return (ready.Count, Unmastered(pool, direction).Count);
    }

    /// <summary>
    /// The ones just missed, again, right now. Rests are ignored on purpose: a word answered
    /// wrong a minute ago is resting only because it was answered at all, and the minute after a
    /// miss is when the right answer is worth most. Order is kept as given — the order they were
    /// missed in — and everything is asked once.
    /// </summary>
    public async Task<VocabSession> BuildDrillAsync(
        IEnumerable<(int WordId, VocabDirection Direction)> wanted, CancellationToken ct)
    {
        var words = await TestableAsync(null, ct);
        var byId = words.ToDictionary(w => w.Id);

        var questions = wanted
            .Where(item => byId.ContainsKey(item.WordId))
            .Select(item => BuildQuestion(byId[item.WordId], item.Direction, words))
            .ToList();

        if (questions.Count == 0) throw new VocabException("Nothing left to drill — those words are gone.");

        return new VocabSession(questions);
    }

    /// <summary>
    /// What each direction has left to ask. The start button is driven by this, so a direction
    /// whose words are all resting can say so instead of failing when the round is built.
    /// </summary>
    public async Task<IReadOnlyList<Availability>> AvailabilityAsync(string? tag, CancellationToken ct)
    {
        var words = await TestableAsync(tag, ct);
        var unique = PromptCounts(words);

        var allowance = SessionBudget.RemainingIntake(await IntroducedTodayAsync(ct));

        return
        [
            .. Enum.GetValues<VocabDirection>()
                .Select(direction => Availability.From(
                        direction.ToString(),
                        words.Where(w => Directions(w, unique).Contains(direction))
                             .Select(w => Standing(w, direction)))
                    .WithIntake(allowance))
        ];
    }

    private async Task<List<VocabWord>> TestableAsync(string? tag, CancellationToken ct)
    {
        var words = await db.VocabWords
            .Include(w => w.Progress)
            .Where(w => !w.NeedsReview)          // a guessed reading must never be drilled in
            .AsNoTracking()
            .ToListAsync(ct);

        return tag is { Length: > 0 } ? words.Where(w => w.Tags.Contains(tag)).ToList() : words;
    }

    public static Availability.Standing Standing(VocabWord word, VocabDirection direction)
    {
        var ladder = Ladder(Progress(word, direction));

        // Retired by hand reads as mastered everywhere, so the bars, the start button and the
        // trainer never disagree about what is left to ask
        if (word.RetiredAt is not null)
            return new Availability.Standing(ladder, (ladder.MasteryStreak, null));

        return new Availability.Standing(
            ladder, Progress(word, direction) is { } p ? (p.ConsecutiveCorrect, p.LastSeenAt) : null);
    }

    /// <summary>A word that has been missed in this direction climbs the longer ladder.</summary>
    internal static RestSchedule Ladder(VocabProgress? progress) =>
        RestSchedule.ForVocabulary(progress?.WrongCount ?? 0);

    private static Availability Tally(Dictionary<VocabDirection, List<VocabWord>> pools) =>
        Availability.From(
            "selected",
            pools.SelectMany(pair => pair.Value.Select(word => Standing(word, pair.Key))));

    public async Task<VocabAnswerResult> AnswerAsync(
        Guid token, string? text, CancellationToken ct)
    {
        var state = Lookup(token);

        var word = await db.VocabWords
            .Include(w => w.Progress)
            .FirstOrDefaultAsync(w => w.Id == state.WordId, ct)
            ?? throw new VocabException("That word no longer exists.");

        var expected = Answer(word, state.Direction);
        var (correct, note) = Judge(word, state.Direction, expected, text);

        // Answering the question the round asked a moment ago rather than the one on screen is a
        // slip of attention, not a gap in knowledge. It gets one free retry, and nothing about
        // the real answer is given away with it.
        if (!correct && !state.Answered && !state.Retried
            && WrongDirection(word, state.Direction, text) is { } gave)
        {
            state.Retried = true;
            return new VocabAnswerResult(false, word.Id, "", "", $"Wrong direction — that's the {gave}.", Retry: true);
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

        return new VocabAnswerResult(correct, word.Id, expected, word.Characters, note);
    }

    private static (bool Correct, string? Note) Judge(
        VocabWord word, VocabDirection direction, string expected, string? text)
    {
        var given = text ?? "";

        return direction switch
        {
            // Assembled from tiles: right characters in the wrong order is a different mistake
            // from the wrong characters, and worth saying so
            VocabDirection.PinyinToCharacters or VocabDirection.EnglishToCharacters =>
                given.Trim() == word.Characters
                    ? (true, null)
                    : (false, SameCharacters(word.Characters, given) ? "Right characters, wrong order." : null),

            VocabDirection.CharactersToPinyin or VocabDirection.EnglishToPinyin =>
                AnswerCheck.PinyinMatches(expected, given)
                    ? (true, null)
                    : (false, AnswerCheck.IsToneOnlyMistake(expected, given)
                        ? "Right syllables, wrong tone."
                        : null),

            _ => (AnswerCheck.EnglishMatches(expected, given), null),
        };
    }

    private static bool SameCharacters(string expected, string given) =>
        TileBank.Characters(expected).OrderBy(c => c, StringComparer.Ordinal)
            .SequenceEqual(TileBank.Characters(given.Trim()).OrderBy(c => c, StringComparer.Ordinal));

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

        var tiles = IsTyped(direction) ? null : TileBank.Build(word, pool);

        return new VocabQuestion(
            token,
            direction,
            Prompt(word, direction),
            Label(PromptForm(direction)),
            Label(AnswerForm(direction)),
            IsTyped(direction),
            tiles);
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

    /// <summary>
    /// How many word-directions were met for the first time today. A direction's first answer is
    /// the oldest one it has, so the day that answer landed on is the day it was introduced.
    /// </summary>
    private async Task<int> IntroducedTodayAsync(CancellationToken ct)
    {
        var since = SessionBudget.TodayStartedAt;

        return await db.VocabAnswers
            .GroupBy(a => new { a.VocabWordId, a.Direction })
            .Select(g => g.Min(a => a.AnsweredAt))
            .CountAsync(first => first >= since, ct);
    }

    /// <summary>
    /// Producing is harder than recognising, and the two are not independent: writing 学校 for
    /// "school" settles whether 学校 means school. So a correct answer in a producing direction
    /// advances its recognising twin by a rung instead of asking it separately — which is what
    /// made one word cost up to thirty questions before it was finished.
    ///
    /// Only for a twin that has never been missed. Once a direction has been got wrong it has to
    /// be earned back directly: that is the whole reason the two ladders exist.
    /// </summary>
    private static readonly IReadOnlyDictionary<VocabDirection, VocabDirection> Covers =
        new Dictionary<VocabDirection, VocabDirection>
        {
            [VocabDirection.EnglishToCharacters] = VocabDirection.CharactersToEnglish,
            [VocabDirection.PinyinToCharacters] = VocabDirection.CharactersToPinyin,
            [VocabDirection.EnglishToPinyin] = VocabDirection.PinyinToEnglish,
        };

    private static void Credit(VocabWord word, VocabDirection direction)
    {
        if (!Covers.TryGetValue(direction, out var eased)) return;

        var progress = Progress(word, eased);

        // Never asked, or missed at least once: the credit is not enough on its own. A direction
        // you have never tried is not one this can quietly finish for you.
        if (progress is null || progress.WrongCount > 0) return;
        if (Ladder(progress).IsMastered(progress.ConsecutiveCorrect)) return;

        progress.ConsecutiveCorrect++;
        progress.LastSeenAt = DateTime.UtcNow;
    }

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
            Credit(word, direction);
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
    /// A direction with nothing ready is simply skipped: the start button knows this in advance
    /// and says so, so there is no reason to cut a rest short behind your back.
    /// </summary>
    private static List<VocabWord> Askable(List<VocabWord> words, VocabDirection direction) =>
        words.Where(w => w.RetiredAt is null
                         && (Progress(w, direction) is not { } p
                             || Ladder(p).IsAvailable(p.ConsecutiveCorrect, p.LastSeenAt)))
             .ToList();

    internal static VocabProgress? Progress(VocabWord word, VocabDirection direction) =>
        word.Progress.FirstOrDefault(p => p.Direction == direction);

    private static double Weight(VocabWord word, VocabDirection direction, MissTally misses) =>
        Progress(word, direction) is { } progress
            ? DrawWeight.For(
                Ladder(progress),
                misses.For(word.Id, (int)direction),
                progress.ConsecutiveCorrect,
                progress.LastSeenAt)
            : DrawWeight.Unseen;

    /// <summary>What has been going wrong lately, one decayed score per word and direction.</summary>
    private async Task<MissTally> RecentMissesAsync(CancellationToken ct)
    {
        var since = MissTally.Since;

        var misses = await db.VocabAnswers
            .Where(a => !a.Correct && a.AnsweredAt >= since)
            .Select(a => new { a.VocabWordId, a.Direction, a.AnsweredAt })
            .ToListAsync(ct);

        return MissTally.From(misses.Select(m => (m.VocabWordId, (int)m.Direction, m.AnsweredAt)));
    }

    private QuestionState Lookup(Guid token) =>
        cache.TryGetValue(CacheKey(token), out QuestionState? state) && state is not null
            ? state
            : throw new VocabException("This round has expired. Start a new one.");

    private static string CacheKey(Guid token) => $"vocab:{token}";
}
