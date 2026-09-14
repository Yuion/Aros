namespace Aros.Api.Scheduling;

/// <summary>
/// How long an item stays out of the pool after each correct answer in a row, and how many in a
/// row finish it for good. Each trainer has two ladders rather than one: a clean run climbs fast,
/// and an item missed even once takes a longer road to the same place, because after a miss a
/// streak of the same length is weaker evidence that the item has stuck.
/// </summary>
public sealed record RestSchedule(int FirstRestStreak, IReadOnlyList<TimeSpan> Rests)
{
    /// <summary>Streak at which an item is done: it leaves the pool and is not asked again.</summary>
    public int MasteryStreak => FirstRestStreak + Rests.Count;

    /// <summary>
    /// Vocabulary, for a word never missed in this direction. Rests begin at the first correct
    /// answer and the first two steps are hours rather than days: right once means it is sticking,
    /// not that it is learned, so it is held back for the session rather than the week.
    ///
    /// Four rungs, not five. Four out of five item-directions in the library have never once been
    /// wrong, and they were sitting at a streak of 4.3 out of 5 — a fifth pass over something never
    /// missed buys almost nothing and costs a question every time. A word that has never been
    /// wrong now masters in four correct answers per direction instead of five, and the rung that
    /// was 24 hours is 48.
    /// </summary>
    public static readonly RestSchedule VocabularyClean = new(
        FirstRestStreak: 1,
        Rests:
        [
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(48),
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(28),
        ]);

    /// <summary>
    /// Vocabulary, for a word missed at least once in this direction. The ladder is the clean one
    /// shifted a rung down and given an extra step: after a miss the same streak is weaker
    /// evidence, so it buys less rest and mastery costs two more correct answers.
    /// </summary>
    public static readonly RestSchedule VocabularyLapsed = new(
        FirstRestStreak: 1,
        Rests:
        [
            TimeSpan.Zero,                 // right once after a miss earns nothing yet
            TimeSpan.FromHours(12),
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(72),
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(14),
            TimeSpan.FromDays(28),
        ]);

    /// <summary>A word's ladder depends on whether it has ever been missed in that direction.</summary>
    public static RestSchedule ForVocabulary(int wrongCount) =>
        wrongCount > 0 ? VocabularyLapsed : VocabularyClean;

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
