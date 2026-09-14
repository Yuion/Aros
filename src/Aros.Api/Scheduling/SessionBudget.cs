namespace Aros.Api.Scheduling;

/// <summary>
/// What one sitting is allowed to cost.
///
/// Without this a sweep asks everything the ladders happen to return, which is unbounded and grows
/// with every lesson: 157 questions were due on the evening this was written, at two to three
/// answers a minute. The ladders decide *what* is worth asking; this decides *how much of it* fits
/// in an evening. Nothing is dropped — an item that does not fit stays due and is drawn first
/// tomorrow, because the pick is weighted by how overdue an item is.
///
/// The numbers come from the answer log: sittings ran 60–65 minutes at 2–3 answers a minute, so
/// sixty questions is about twenty-five minutes across the three trainers.
/// </summary>
public static class SessionBudget
{
    /// <summary>
    /// Listening is asked one mode at a time, so this is per mode and a pass over all four costs
    /// up to four times it. Fifteen holds a pass over all four to sixty questions, and the second
    /// pass of the day is mostly empty anyway: everything answered in the first is resting by then.
    /// </summary>
    public const int Listening = 15;

    /// <summary>Vocabulary asks every direction in one round, so this is the whole round.</summary>
    public const int Vocabulary = 20;

    public const int Grammar = 15;

    /// <summary>
    /// The mixed daily session: everything due, of every kind, in one game. Sixty questions is
    /// about twenty-five minutes at the pace the answer log shows, and it replaces going through
    /// the three trainers one at a time rather than adding to them.
    /// </summary>
    public const int Daily = 60;

    /// <summary>
    /// How many items may be met for the first time in one day, per trainer.
    ///
    /// A new mode has no history, so every item in it is due at once — turning Ordering on put 75
    /// questions in the queue in one evening, and the grammar trainer another 34. An item never
    /// asked is new, not overdue, and new material is what a lesson is for: six a day is a
    /// lesson's worth, and the backlog ramps in instead of landing whole.
    /// </summary>
    public const int NewPerDay = 6;

    /// <summary>The budget never raises a count, only lowers it.</summary>
    public static int Cap(int wanted, int budget) => Math.Min(wanted, budget);

    /// <summary>
    /// The pool a round may draw on: everything already met, plus at most <paramref name="allowance"/>
    /// items being met for the first time. The new ones are taken in the order the pool gives them,
    /// which is the order they were added, so a lesson's own sentences come up before older
    /// leftovers.
    /// </summary>
    public static List<T> WithIntake<T>(IEnumerable<T> pool, Func<T, bool> isNew, int allowance)
    {
        var kept = new List<T>();
        var taken = 0;

        foreach (var item in pool)
        {
            if (!isNew(item)) { kept.Add(item); continue; }
            if (taken >= allowance) continue;

            kept.Add(item);
            taken++;
        }

        return kept;
    }

    /// <summary>
    /// How many first meetings are still allowed today, given how many already happened.
    /// The day is the local one: a session that runs past midnight is still tonight's session as
    /// far as the person doing it is concerned, but the count is cheap and the edge is harmless.
    /// </summary>
    public static int RemainingIntake(int introducedToday) =>
        Math.Max(0, NewPerDay - introducedToday);

    /// <summary>Local midnight, as UTC, for counting what today has already introduced.</summary>
    public static DateTime TodayStartedAt => DateTime.Today.ToUniversalTime();
}
