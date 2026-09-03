namespace Aros.Api.Data.Entities;

/// <summary>
/// Listening-practice score for one clip in one mode. Drives how often the clip is drawn:
/// misses push it up, a streak of correct answers pushes it down.
///
/// Per mode, not per clip, for the reason the vocabulary trainer scores per direction —
/// recognising a sentence among three and writing out what you heard are different skills, and
/// one score for both would hide the weaker one.
/// </summary>
public class TtsClipStat
{
    public int Id { get; set; }
    public int TtsClipId { get; set; }
    public ListeningMode Mode { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int ConsecutiveCorrect { get; set; }       // Reset to 0 on every miss
    public DateTime? LastSeenAt { get; set; }

    public TtsClip TtsClip { get; set; } = null!;
}
