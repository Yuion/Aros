namespace Aros.Api.Data.Entities;

public class TtsClip
{
    public int Id { get; set; }
    public string Sentence { get; set; } = "";        // Normalized Chinese text — the cache key
    public string Location { get; set; } = "";        // Audio file name, relative to Tts:MediaPath
    public string Voice { get; set; } = "";           // Narakeet voice that produced the file
    public double? DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TtsClipStat? Stat { get; set; }
}
