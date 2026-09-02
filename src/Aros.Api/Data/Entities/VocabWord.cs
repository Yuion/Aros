namespace Aros.Api.Data.Entities;

/// <summary>
/// One vocabulary item — a single character or a multi-character word, harvested from the
/// sentences in the TTS library. Unique on (Characters, Pinyin), so 多音字 like 行/xing2 and
/// 行/hang2 are separate entries with separate scores.
/// </summary>
public class VocabWord
{
    public int Id { get; set; }
    public string Characters { get; set; } = "";
    public string Pinyin { get; set; } = "";              // tone numbers, space-separated, v for ü
    public string English { get; set; } = "";             // alternatives separated by /
    public List<string> Tags { get; set; } = [];
    public string? Notes { get; set; }

    /// <summary>
    /// Set when the harvest could not be sure of the entry — several dictionary readings to
    /// choose between, or no dictionary match at all. Held out of tests until confirmed, so a
    /// guessed reading is never drilled in.
    /// </summary>
    public bool NeedsReview { get; set; }

    /// <summary>The other readings the dictionary offered, for the review screen to choose from.</summary>
    public string? ReadingAlternatives { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VocabProgress> Progress { get; set; } = [];
}
