namespace Aros.Api.Scheduling;

/// <summary>
/// How badly an item is going *lately*, rather than how badly it ever went.
///
/// The draw weight used to count misses for the life of an item, which says the wrong thing about
/// a word that was a mess in its first week and has been right ever since: the early failures kept
/// it near the top of every round long after they stopped being true. Here each miss is worth
/// <c>0.5 ^ (age / half-life)</c>, so a miss yesterday counts as one, a three-week-old miss as a
/// half, and one from two months ago as an eighth. Trouble that is current stays current; trouble
/// that was outgrown fades on its own, with nothing to reset by hand.
///
/// This decides how *often* an item comes up. Which ladder it climbs is still decided by whether
/// it has ever been missed (<see cref="RestSchedule.ForVocabulary"/>), because that is a question
/// about how much a streak proves, and an old failure still means the item was once hard.
/// </summary>
public sealed class MissTally
{
    /// <summary>Days for a miss to count half as much. Three weeks: a month of clean answers all but clears it.</summary>
    public const double HalfLifeDays = 21.0;

    /// <summary>Beyond this a miss is worth under a tenth and is not worth loading.</summary>
    public const int WindowDays = 120;

    public static readonly MissTally Empty = new([]);

    private readonly Dictionary<(int Id, int Part), double> _scores;

    private MissTally(Dictionary<(int, int), double> scores) => _scores = scores;

    /// <summary>The oldest miss worth counting, for the query that loads them.</summary>
    public static DateTime Since => DateTime.UtcNow.AddDays(-WindowDays);

    /// <summary>
    /// Adds up the decayed weight of every miss, per item. <paramref name="part"/> is the
    /// direction or mode: misses are scored per skill, the way rests and streaks are.
    /// </summary>
    public static MissTally From(IEnumerable<(int Id, int Part, DateTime At)> misses)
    {
        var now = DateTime.UtcNow;
        var scores = new Dictionary<(int, int), double>();

        foreach (var (id, part, at) in misses)
        {
            var age = Math.Max(0, (now - at).TotalDays);
            var worth = Math.Pow(0.5, age / HalfLifeDays);

            scores[(id, part)] = scores.GetValueOrDefault((id, part)) + worth;
        }

        return new MissTally(scores);
    }

    /// <summary>What this item has been getting wrong lately. Zero if it has not.</summary>
    public double For(int id, int part = 0) => _scores.GetValueOrDefault((id, part));
}
