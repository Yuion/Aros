using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Listening;
using Aros.Api.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aros.Api.Grammar;

public record GrammarQuestion(
    Guid Token,
    int PointId,
    string Pattern,
    string Prompt,
    IReadOnlyList<string> Tiles);

public record GrammarRound(IReadOnlyList<GrammarQuestion> Questions);

public record GrammarAnswerResult(
    bool Correct, int PointId, string Pattern, string Expected, string? Note);

public class GrammarException(string message) : Exception(message);

/// <summary>
/// The grammar trainer. A word is asked one way and a sentence another; a pattern can only be
/// asked by making you build something with it, so every question is the same shape: here is the
/// English, put the Chinese together from tiles. Word order is the whole point, and tiles are the
/// only way to ask for it without an IME.
///
/// Scheduling is per point, not per sentence. Six correct answers to six different sentences that
/// use 也 is evidence about 也; the same sentence six times is evidence about that sentence.
/// </summary>
public class GrammarService(AppDbContext db, IMemoryCache cache)
{
    public const int DefaultCount = 10;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(2);

    /// <summary>A pattern climbs the vocabulary ladder: the same evidence buys the same rest.</summary>
    private static RestSchedule Ladder(GrammarProgress? progress) =>
        RestSchedule.ForVocabulary(progress?.WrongCount ?? 0);

    private sealed class QuestionState
    {
        public required int ItemId { get; init; }
        public required int PointId { get; init; }
        public bool Answered { get; set; }
    }

    public async Task<GrammarRound> BuildRoundAsync(int count, bool sweep, CancellationToken ct)
    {
        var (points, items, progress) = await PoolAsync(ct);

        if (items.Count == 0)
            throw new GrammarException(
                "No grammar drills yet. They are built from the exercises and examples your lessons "
                + "recorded — press Rebuild, or finish a lesson first.");

        var askable = points
            .Where(p => items.Any(i => i.GrammarPointId == p.Id))
            .Where(p => Standing(p, progress).Schedule.IsAvailable(
                Streak(p, progress), progress.GetValueOrDefault(p.Id)?.LastSeenAt))
            .ToList();

        if (askable.Count == 0) throw new GrammarException(NothingToAsk(points, items, progress));

        // A pattern never drilled is new, not overdue: the trainer opened with all 34 due at once
        var intake = SessionBudget.RemainingIntake(await IntroducedTodayAsync(ct));
        askable = SessionBudget.WithIntake(askable, p => !progress.ContainsKey(p.Id), intake);

        if (askable.Count == 0)
            throw new GrammarException(
                $"Today's {SessionBudget.NewPerDay} new patterns are done. The rest are waiting for tomorrow.");

        var wanted = SessionBudget.Cap(
            sweep ? askable.Count : Math.Clamp(count, 1, askable.Count), SessionBudget.Grammar);

        var drawn = DrawWeight.PickWorstFirst(
            askable, wanted, point => Weight(point, progress));

        var sentences = items.Select(i => i.Answer).Distinct().ToList();
        var homophones = await HomophonesAsync(ct);

        var questions = drawn
            .Select(point => Ask(point, items, sentences, homophones))
            .ToList();

        return new GrammarRound(questions);
    }

    /// <summary>How many patterns were drilled for the first time today.</summary>
    private async Task<int> IntroducedTodayAsync(CancellationToken ct)
    {
        var since = SessionBudget.TodayStartedAt;

        return await db.GrammarAnswers
            .GroupBy(a => a.GrammarPointId)
            .Select(g => g.Min(a => a.AnsweredAt))
            .CountAsync(first => first >= since, ct);
    }

    /// <summary>
    /// One question for one pattern: the sentence least recently asked of the ones that exercise
    /// it, so a pattern with six drills cycles through all six rather than leaning on one.
    /// </summary>
    private GrammarQuestion Ask(
        GrammarPoint point,
        List<GrammarItem> items,
        List<string> sentences,
        Dictionary<string, string> homophones)
    {
        var candidates = items.Where(i => i.GrammarPointId == point.Id).ToList();
        var item = candidates[Random.Shared.Next(candidates.Count)];

        var token = Guid.NewGuid();
        cache.Set(CacheKey(token), new QuestionState { ItemId = item.Id, PointId = point.Id }, TokenLifetime);

        return new GrammarQuestion(
            token,
            point.Id,
            point.Title,
            item.Prompt,
            SentenceTiles.Build(item.Answer, sentences, homophones));
    }

    public async Task<GrammarAnswerResult> AnswerAsync(Guid token, string? text, CancellationToken ct)
    {
        var state = Lookup(token);

        var item = await db.GrammarItems
            .Include(i => i.Point)
            .FirstOrDefaultAsync(i => i.Id == state.ItemId, ct)
            ?? throw new GrammarException("That drill no longer exists.");

        var given = text ?? "";
        var correct = SentenceTiles.Matches(item.Answer, given);

        var note = correct || !SentenceTiles.SameCharacters(item.Answer, given)
            ? null
            : "Right characters, wrong order.";

        if (!state.Answered)
        {
            state.Answered = true;
            await RecordAsync(item, correct, ct);
        }

        return new GrammarAnswerResult(correct, item.GrammarPointId, item.Point?.Title ?? "", item.Answer, note);
    }

    private async Task RecordAsync(GrammarItem item, bool correct, CancellationToken ct)
    {
        var progress = await db.GrammarProgress.FirstOrDefaultAsync(p => p.GrammarPointId == item.GrammarPointId, ct);

        if (progress is null)
        {
            progress = new GrammarProgress { GrammarPointId = item.GrammarPointId };
            db.GrammarProgress.Add(progress);
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

        db.GrammarAnswers.Add(new GrammarAnswer
        {
            GrammarPointId = item.GrammarPointId,
            GrammarItemId = item.Id,
            Correct = correct,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>What the trainer has left to ask, in the shape both trainers report.</summary>
    public async Task<Availability> AvailabilityAsync(CancellationToken ct)
    {
        var (points, items, progress) = await PoolAsync(ct);

        return Availability.From(
            "Grammar",
            points.Where(p => items.Any(i => i.GrammarPointId == p.Id)).Select(p => Standing(p, progress)));
    }

    /// <summary>Every pattern with its record — the page behind the start button.</summary>
    public async Task<IReadOnlyList<(GrammarPoint Point, GrammarProgress? Progress, int Items, string State)>>
        OverviewAsync(CancellationToken ct)
    {
        var (points, items, progress) = await PoolAsync(ct);

        return
        [
            .. points.Select(point =>
            {
                var mine = progress.GetValueOrDefault(point.Id);
                var drills = items.Count(i => i.GrammarPointId == point.Id);
                var ladder = Ladder(mine);
                var streak = Streak(point, progress);

                var state =
                    drills == 0 ? "unavailable"
                    : ladder.IsMastered(streak) ? "mastered"
                    : ladder.IsResting(streak, mine?.LastSeenAt) ? "resting"
                    : "ready";

                return (point, mine, drills, state);
            })
        ];
    }

    private async Task<(List<GrammarPoint> Points, List<GrammarItem> Items, Dictionary<int, GrammarProgress> Progress)>
        PoolAsync(CancellationToken ct)
    {
        var points = await db.GrammarPoints.AsNoTracking().OrderBy(p => p.IntroducedInLesson).ThenBy(p => p.Id).ToListAsync(ct);
        var items = await db.GrammarItems.AsNoTracking().ToListAsync(ct);
        var progress = await db.GrammarProgress.AsNoTracking().ToDictionaryAsync(p => p.GrammarPointId, ct);

        return (points, items, progress);
    }

    private static Availability.Standing Standing(GrammarPoint point, Dictionary<int, GrammarProgress> progress)
    {
        var mine = progress.GetValueOrDefault(point.Id);

        return new Availability.Standing(
            Ladder(mine),
            mine is null ? null : (mine.ConsecutiveCorrect, mine.LastSeenAt));
    }

    private static int Streak(GrammarPoint point, Dictionary<int, GrammarProgress> progress) =>
        progress.GetValueOrDefault(point.Id)?.ConsecutiveCorrect ?? 0;

    private static double Weight(GrammarPoint point, Dictionary<int, GrammarProgress> progress) =>
        progress.GetValueOrDefault(point.Id) is { } mine
            ? DrawWeight.For(Ladder(mine), mine.WrongCount, mine.ConsecutiveCorrect, mine.LastSeenAt)
            : DrawWeight.Unseen;

    private static string NothingToAsk(
        List<GrammarPoint> points, List<GrammarItem> items, Dictionary<int, GrammarProgress> progress)
    {
        var tally = Availability.From(
            "Grammar",
            points.Where(p => items.Any(i => i.GrammarPointId == p.Id)).Select(p => Standing(p, progress)));

        return tally.NextDueAt is { } due
            ? $"Every pattern is resting. The next is due {Availability.Due(due)}."
            : "Every pattern with drills is mastered. The next lesson will bring more.";
    }

    private async Task<Dictionary<string, string>> HomophonesAsync(CancellationToken ct)
    {
        var groups = await db.HomophoneGroups.AsNoTracking().ToListAsync(ct);
        var lookup = new Dictionary<string, string>();

        foreach (var group in groups)
            foreach (var rune in Homophones.Runes(group.Characters))
                lookup.TryAdd(rune.ToString(), group.Characters);

        return lookup;
    }

    private QuestionState Lookup(Guid token) =>
        cache.TryGetValue(CacheKey(token), out QuestionState? state) && state is not null
            ? state
            : throw new GrammarException("This round has expired. Start a new one.");

    private static string CacheKey(Guid token) => $"grammar:{token}";
}
