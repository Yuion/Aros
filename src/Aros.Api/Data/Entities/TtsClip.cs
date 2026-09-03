namespace Aros.Api.Data.Entities;

public class TtsClip
{
    public int Id { get; set; }
    public string Sentence { get; set; } = "";        // Normalized Chinese text — the cache key
    public string Location { get; set; } = "";        // Audio file name, relative to Tts:MediaPath
    public string Voice { get; set; } = "";           // Narakeet voice that produced the file
    public double? DurationSeconds { get; set; }

    // Filled in when a sentence arrives with them; blank on anything typed straight into the
    // TTS box. A mode that needs one of these simply skips the sentences that lack it.
    public string Pinyin { get; set; } = "";          // Tone-numbered, spaced — same format as vocabulary
    public string English { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>One row per mode practised — hearing and writing are scored apart.</summary>
    public ICollection<TtsClipStat> Stats { get; set; } = [];
}
