namespace Aros.Api.Data.Entities;

/// <summary>
/// Listening-practice score for a single clip. Drives how often the clip is drawn:
/// misses push it up, a streak of correct answers pushes it down.
/// </summary>
public class TtsClipStat
{
    public int Id { get; set; }
    public int TtsClipId { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int ConsecutiveCorrect { get; set; }       // Reset to 0 on every miss
    public DateTime? LastSeenAt { get; set; }

    public TtsClip TtsClip { get; set; } = null!;
}
