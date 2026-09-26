namespace Aros.Api.Data.Entities;

/// <summary>
/// One character written by hand and graded stroke by stroke.
///
/// Deliberately its own table with no link to VocabProgress. Writing 你 badly says nothing
/// about whether you know what 你 means or how it sounds, and letting it feed the schedule
/// would put a word back in rotation for a reason the trainers never tested. Nothing here is
/// read by the planner, the daily session or any trainer: it is a record of practice, and the
/// only place it shows up is the writing page and a line on the vocabulary row.
/// </summary>
public class WritingAttempt
{
    public int Id { get; set; }

    public int WordId { get; set; }
    public VocabWord? Word { get; set; }

    /// <summary>The single character that was written - a word is practised one at a time.</summary>
    public string Character { get; set; } = "";

    /// <summary>
    /// <see cref="WritingMode.Copying"/> or <see cref="WritingMode.Memory"/>. Kept apart because
    /// they are different skills: copying a shown character tests your hand, writing it from
    /// memory tests whether you have it at all. Averaging them would hide both.
    /// </summary>
    public string Mode { get; set; } = WritingMode.Memory;

    /// <summary>Strokes rejected before the right one landed. Zero is a clean character.</summary>
    public int Mistakes { get; set; }

    /// <summary>
    /// A stroke that was the right shape in the right place but travelled the wrong way. Worth
    /// keeping apart from an ordinary miss: it is the mistake a printed grid cannot catch you at.
    /// </summary>
    public bool Backwards { get; set; }

    public int Strokes { get; set; }
    public int DurationMs { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;
}

public static class WritingMode
{
    /// <summary>The stroke order is on screen while you write.</summary>
    public const string Copying = "copying";

    /// <summary>Nothing to look at but the empty box.</summary>
    public const string Memory = "memory";

    public static bool IsValid(string mode) => mode is Copying or Memory;
}
