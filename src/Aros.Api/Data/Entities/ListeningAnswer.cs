namespace Aros.Api.Data.Entities;

/// <summary>
/// One answered question. TtsClipStat keeps the running totals the game needs; this keeps the
/// history those totals can't reconstruct — when you practiced, and how you were doing then.
/// </summary>
public class ListeningAnswer
{
    public int Id { get; set; }
    public int TtsClipId { get; set; }
    public ListeningMode Mode { get; set; }
    public bool Correct { get; set; }

    /// <summary>
    /// What was actually typed or assembled, trimmed, or null for an answer recorded before this
    /// was kept. Correct/incorrect says a mistake happened; this says what the mistake was, which
    /// is the difference between "ni2 for ni3" and not knowing the word at all. Nothing schedules
    /// on it - it is there to be read, by you and by the tutor.
    /// </summary>
    public string? Given { get; set; }

    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;

    public TtsClip TtsClip { get; set; } = null!;
}
