namespace Aros.Api.Scheduling;

/// <summary>
/// How one item stands, for the lists that show a library rather than run a round. The trainers
/// answer "what can I ask next"; a list has to answer "why is this one not being asked", which is
/// a different question and needs the reason, not just the count.
///
/// Both libraries report it in the same shape, so the sentence list and the word list can be
/// filtered and read the same way.
/// </summary>
public static class ItemState
{
    /// <summary>One direction or mode of one item.</summary>
    public sealed record Part(
        string key,
        string state,
        int correct,
        int wrong,
        int streak,
        DateTime? lastSeenAt,
        DateTime? dueAt,
        string? due);

    public static Part Describe(
        string key,
        DateTime? retiredAt,
        bool possible,
        RestSchedule ladder,
        int streak,
        int correct,
        int wrong,
        DateTime? lastSeenAt)
    {
        var practised = correct + wrong > 0;
        var restingUntil = practised ? ladder.RestingUntil(streak, lastSeenAt) : null;
        var resting = restingUntil is { } until && until > DateTime.UtcNow;

        // In the order that decides it: retired by hand, unable to be asked here at all, finished
        // on the ladder, waiting out a rest, otherwise askable now
        var state =
            retiredAt is not null ? "retired"
            : !possible ? "unavailable"
            : ladder.IsMastered(streak) ? "mastered"
            : resting ? "resting"
            : "ready";

        return new Part(
            key, state, correct, wrong, streak, lastSeenAt,
            resting ? restingUntil : null,
            resting ? Availability.Due(restingUntil!.Value) : null);
    }

    /// <summary>
    /// The one word for the whole item, which is what a filter acts on. Anything still askable
    /// somewhere counts as in rotation: a word mastered in five directions and shaky in the sixth
    /// is not finished, and hiding it would hide the very direction worth practising.
    /// </summary>
    public static string Overall(DateTime? retiredAt, IEnumerable<string> parts)
    {
        if (retiredAt is not null) return "retired";

        var states = parts.ToList();

        if (states.Any(s => s == "ready")) return "ready";
        if (states.Any(s => s == "resting")) return "resting";

        return states.Any(s => s == "mastered") ? "mastered" : "unavailable";
    }
}
