namespace Aros.Api.Data.Entities;

/// <summary>
/// One syllable heard and a tone named for it.
///
/// Keyed by the sound rather than by a word, because that is what is being tested: whether cài
/// and cāi can be told apart by ear, not whether 菜 is known. Nothing here reaches the
/// vocabulary, listening or grammar schedules — hearing a tone is its own skill, and getting it
/// wrong says nothing about whether a word is known.
/// </summary>
public class ToneAnswer
{
    public int Id { get; set; }

    /// <summary>The syllable without its tone: "cai".</summary>
    public string Syllable { get; set; } = "";

    /// <summary>The tone it was actually said in, 1 to 4.</summary>
    public int Tone { get; set; }

    /// <summary>The tone that was picked. Kept even when right, so hesitation patterns survive.</summary>
    public int Given { get; set; }

    public bool Correct { get; set; }
    public int DurationMs { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
