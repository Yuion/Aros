namespace Aros.Api.Scheduling;

/// <summary>
/// How often a practice item comes back. Shared by the listening game and the vocabulary
/// trainer so the two can never drift apart.
///
/// Misses raise the weight; a correct streak lowers it; time erodes the streak, so nothing
/// stays suppressed merely because it was known once. Score and recency end up as one number.
///
/// On top of that sits an expanding rest schedule: five correct answers in a row take an item
/// out of the pool for a week, and each further correct answer doubles the wait until it is
/// finally mastered and leaves the pool for good. The weight decides *how often* something
/// comes up; the rest decides *whether* it comes up at all.
/// </summary>
public static class DrawWeight
{
    /// <summary>Days without practice that cancel one correct answer from a streak.</summary>
    public const double StreakDecayDays = 7.0;

    /// <summary>The weight never reaches zero — the rest schedule, not the weight, retires an item.</summary>
    public const double Floor = 0.25;

    /// <summary>Never practiced — fully due.</summary>
    public const double Unseen = 1.0;

    /// <summary>Correct answers in a row that first send an item to rest.</summary>
    public const int RestStreak = 5;

    /// <summary>How long each rest lasts, for streaks <see cref="RestStreak"/> upwards.</summary>
    private static readonly int[] RestLengths = [7, 14, 28];

    /// <summary>Streak at which an item is done: it leaves the pool and is not asked again.</summary>
    public const int MasteryStreak = RestStreak + 3;   // 5 correct, then 6, 7, and the 8th masters it

    public static double For(int wrongCount, int consecutiveCorrect, DateTime? lastSeenAt)
    {
        var weight = (1 + wrongCount) * Math.Pow(0.5, EffectiveStreak(consecutiveCorrect, lastSeenAt));
        return Math.Max(Floor, weight);
    }

    public static bool IsMastered(int consecutiveCorrect) => consecutiveCorrect >= MasteryStreak;

    /// <summary>How long this streak rests, or null if it is too short to rest or already mastered.</summary>
    public static int? RestDays(int consecutiveCorrect) =>
        consecutiveCorrect < RestStreak || IsMastered(consecutiveCorrect)
            ? null
            : RestLengths[consecutiveCorrect - RestStreak];

    public static DateTime? RestingUntil(int consecutiveCorrect, DateTime? lastSeenAt) =>
        RestDays(consecutiveCorrect) is { } days && lastSeenAt is { } seen
            ? seen.AddDays(days)
            : null;

    public static bool IsResting(int consecutiveCorrect, DateTime? lastSeenAt) =>
        RestingUntil(consecutiveCorrect, lastSeenAt) is { } until && until > DateTime.UtcNow;

    /// <summary>Can be asked right now: not mastered, and not in the middle of a rest.</summary>
    public static bool IsAvailable(int consecutiveCorrect, DateTime? lastSeenAt) =>
        !IsMastered(consecutiveCorrect) && !IsResting(consecutiveCorrect, lastSeenAt);

    private static double EffectiveStreak(int consecutiveCorrect, DateTime? lastSeenAt)
    {
        if (consecutiveCorrect <= 0) return 0;
        if (lastSeenAt is null) return 0;          // no date to decay from — treat as due

        // A rest that has run its course leaves the item fully due. The point of resting is that
        // the item comes back to be tested, not that it quietly fades out of reach — suppressing
        // it further would stall the streak and it would never reach the next step.
        if (RestDays(consecutiveCorrect) is not null) return 0;

        var daysSince = Math.Max(0, (DateTime.UtcNow - lastSeenAt.Value).TotalDays);
        return Math.Max(0, consecutiveCorrect - daysSince / StreakDecayDays);
    }

    /// <summary>
    /// Draws <paramref name="count"/> distinct items, each item's chance proportional to its weight.
    /// </summary>
    public static List<T> PickWithoutReplacement<T>(IEnumerable<T> items, int count, Func<T, double> weight)
    {
        var remaining = items.ToList();
        var picked = new List<T>(Math.Min(count, remaining.Count));

        while (picked.Count < count && remaining.Count > 0)
        {
            var weights = remaining.Select(weight).ToList();
            var roll = Random.Shared.NextDouble() * weights.Sum();

            var index = 0;
            for (; index < weights.Count - 1; index++)
            {
                roll -= weights[index];
                if (roll <= 0) break;
            }

            picked.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return picked;
    }
}
