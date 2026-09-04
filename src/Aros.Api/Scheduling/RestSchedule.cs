namespace Aros.Api.Scheduling;

/// <summary>
/// How long an item stays out of the pool after each correct answer in a row, and how many in a
/// row finish it for good. The trainers no longer share one ladder: the listening trainer climbs
/// faster and takes past failures into account, while the vocabulary trainer starts with two short
/// rests inside the same day.
/// </summary>
public sealed record RestSchedule(int FirstRestStreak, IReadOnlyList<TimeSpan> Rests)
{
    /// <summary>Streak at which an item is done: it leaves the pool and is not asked again.</summary>
    public int MasteryStreak => FirstRestStreak + Rests.Count;

    /// <summary>
    /// The vocabulary ladder. The first two steps are hours, not days: three right in a row means a
    /// word is sticking, not that it is learned, so it is held back for the rest of the session
    /// rather than the rest of the week.
    /// </summary>
    public static readonly RestSchedule Vocabulary = new(
        FirstRestStreak: 3,
        Rests:
        [
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(36),
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            TimeSpan.FromDays(28),
        ]);

    /// <summary>
    /// Listening, for a sentence never missed. Rests begin at the first correct answer and it is
    /// mastered on the fifth — a sentence answered right five times running was never in doubt.
    /// </summary>
    public static readonly RestSchedule ListeningClean = new(
        FirstRestStreak: 1,
        Rests:
        [
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(3),
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
        ]);

    /// <summary>
    /// Listening, for a sentence missed at least once. The early rests are shorter and there is one
    /// more rung before mastery: having got it wrong once, a streak is weaker evidence, so the same
    /// number of correct answers buys less.
    /// </summary>
    public static readonly RestSchedule ListeningLapsed = new(
        FirstRestStreak: 1,
        Rests:
        [
            TimeSpan.Zero,                 // right once after a miss earns nothing yet
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            TimeSpan.FromDays(28),
        ]);

    /// <summary>A sentence's ladder depends on whether it has ever been missed.</summary>
    public static RestSchedule ForListening(int wrongCount) =>
        wrongCount > 0 ? ListeningLapsed : ListeningClean;

    public bool IsMastered(int consecutiveCorrect) => consecutiveCorrect >= MasteryStreak;

    /// <summary>How long this streak rests, or null if it is too short to rest or already mastered.</summary>
    public TimeSpan? RestLength(int consecutiveCorrect) =>
        consecutiveCorrect < FirstRestStreak || IsMastered(consecutiveCorrect)
            ? null
            : Rests[consecutiveCorrect - FirstRestStreak];

    public DateTime? RestingUntil(int consecutiveCorrect, DateTime? lastSeenAt) =>
        RestLength(consecutiveCorrect) is { } rest && lastSeenAt is { } seen
            ? seen + rest
            : null;

    public bool IsResting(int consecutiveCorrect, DateTime? lastSeenAt) =>
        RestingUntil(consecutiveCorrect, lastSeenAt) is { } until && until > DateTime.UtcNow;

    /// <summary>Can be asked right now: not mastered, and not in the middle of a rest.</summary>
    public bool IsAvailable(int consecutiveCorrect, DateTime? lastSeenAt) =>
        !IsMastered(consecutiveCorrect) && !IsResting(consecutiveCorrect, lastSeenAt);

    /// <summary>The longest streak any item on this ladder can show before it is finished.</summary>
    public int LastActiveStreak => MasteryStreak - 1;
}
