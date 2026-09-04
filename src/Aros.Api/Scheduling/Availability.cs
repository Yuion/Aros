namespace Aros.Api.Scheduling;

/// <summary>
/// What one direction or mode has left to ask. <paramref name="Ready"/> is what a round can draw
/// on right now; the other two are why the rest is not available. Both trainers report this in the
/// same shape so the start button and the stats bars can be built the same way.
/// </summary>
public record Availability(string Key, int Ready, int Resting, int Mastered, DateTime? NextDueAt)
{
    public int Total => Ready + Resting + Mastered;

    /// <summary>Nothing left to ask, but only because of rests — it comes back on its own.</summary>
    public bool RestingOut => Ready == 0 && Resting > 0;

    /// <summary>
    /// Counts one pool. An item with no progress row has never been practised, so it is ready.
    /// <paramref name="NextDueAt"/> is the earliest rest to expire, which is the only useful thing
    /// to say when a direction has run dry: not "come back later" but when.
    /// </summary>
    public static Availability From(string key, IEnumerable<(int Streak, DateTime? LastSeenAt)?> progress)
    {
        var ready = 0;
        var resting = 0;
        var mastered = 0;
        DateTime? next = null;

        foreach (var item in progress)
        {
            if (item is not { } p)
            {
                ready++;
                continue;
            }

            if (DrawWeight.IsMastered(p.Streak))
            {
                mastered++;
            }
            else if (DrawWeight.RestingUntil(p.Streak, p.LastSeenAt) is { } until && until > DateTime.UtcNow)
            {
                resting++;
                if (next is null || until < next) next = until;
            }
            else
            {
                ready++;
            }
        }

        return new Availability(key, ready, resting, mastered, next);
    }

    /// <summary>When a rest is up, in words. "Later" helps nobody decide whether to wait.</summary>
    public static string Due(DateTime whenUtc)
    {
        var span = whenUtc - DateTime.UtcNow;

        if (span <= TimeSpan.Zero) return "now";
        if (span < TimeSpan.FromHours(1)) return "within the hour";

        // The two short rests are 12 and 36 hours, so hours stay useful past the day mark —
        // rounding 36 hours to "in 2 days" would misstate it by half a day.
        if (span < TimeSpan.FromHours(48)) return $"in {(int)Math.Ceiling(span.TotalHours)} hours";

        var days = (int)Math.Ceiling(span.TotalDays);
        return $"in {days} days";
    }
}
