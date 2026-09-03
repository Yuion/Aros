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
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;

    public TtsClip TtsClip { get; set; } = null!;
}
