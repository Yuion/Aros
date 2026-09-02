namespace Aros.Api.Scheduling;

/// <summary>
/// How often a practice item comes back. Shared by the listening game and the vocabulary
/// trainer so the two can never drift apart.
///
/// Misses raise the weight; a correct streak lowers it; time erodes the streak, so nothing
/// stays suppressed merely because it was known once. Score and recency end up as one number.
/// </summary>
public static class DrawWeight
{
    /// <summary>Days without practice that cancel one correct answer from a streak.</summary>
    public const double StreakDecayDays = 7.0;

    /// <summary>Nothing ever leaves rotation, however well known.</summary>
    public const double Floor = 0.25;

    public static double For(int wrongCount, int consecutiveCorrect, DateTime? lastSeenAt)
    {
        var weight = (1 + wrongCount) * Math.Pow(0.5, EffectiveStreak(consecutiveCorrect, lastSeenAt));
        return Math.Max(Floor, weight);
    }

    /// <summary>Never practiced — fully due.</summary>
    public const double Unseen = 1.0;

    private static double EffectiveStreak(int consecutiveCorrect, DateTime? lastSeenAt)
    {
        if (consecutiveCorrect <= 0) return 0;
        if (lastSeenAt is null) return 0;          // no date to decay from — treat as due

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
