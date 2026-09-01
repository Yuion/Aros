using System.Text;

namespace Aros.Api.Tts;

public static class ChineseText
{
    /// <summary>
    /// Collapses a raw input into the canonical form used both as the cache key and as the
    /// text sent to Narakeet: NFC-normalized, with every whitespace and zero-width character
    /// stripped. Chinese does not space its words, so this is lossless for the audio while
    /// making "你好 世界" and "你好世界" a single paid synthesis.
    /// Punctuation is deliberately kept — it shapes the reading and the on-screen sentence.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var normalized = input.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(normalized.Length);

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) continue;   // covers U+3000 ideographic space
            if (IsInvisible(rune)) continue;
            sb.Append(rune);
        }

        return sb.ToString();
    }

    private static bool IsInvisible(Rune rune) =>
        rune.Value is 0x00AD or 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF;
}
