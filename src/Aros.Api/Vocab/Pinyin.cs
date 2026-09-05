namespace Aros.Api.Vocab;

/// <summary>
/// The one written form of pinyin this app uses: tone numbers, space-separated syllables, ü as v,
/// neutral tone 5. Lives on its own because it is a spelling convention, not a dictionary — it
/// outlived the CC-CEDICT import that first needed it.
/// </summary>
public static class Pinyin
{
    /// <summary>ü written as v, which is what a phone keyboard can actually produce.</summary>
    public static string Normalize(string raw) =>
        raw.Replace("u:", "v").Replace("U:", "V").Trim();

    /// <summary>Lowercase, single-spaced — the form stored and shown.</summary>
    public static string ForDisplay(string pinyin) =>
        string.Join(' ', Normalize(pinyin)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
