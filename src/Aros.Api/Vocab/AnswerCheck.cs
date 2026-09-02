using System.Text.RegularExpressions;

namespace Aros.Api.Vocab;

/// <summary>
/// Marking rules for typed answers.
/// Pinyin is written with tone numbers, space-separated syllables and ü as v (`ni3 hao3`,
/// `lv4`, neutral tone `de5`). Comparison is case-insensitive and ignores spacing, so
/// `ni3hao3` also passes — spaced remains the form the app displays.
/// </summary>
public static partial class AnswerCheck
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Repeated, not single: stripping one token would leave "the to study" as "to study" while
    /// the stored "to study" became "study", and the two would never meet. Normalisation has to
    /// reach the same result from either side.
    /// </summary>
    [GeneratedRegex(@"^((to|a|an|the)\s+)+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFiller();

    [GeneratedRegex(@"\b(a|an|the)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Articles();

    [GeneratedRegex(@"[.,;!?""'()\[\]]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"[1-5]")]
    private static partial Regex ToneDigits();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthetical();

    public static bool PinyinMatches(string expected, string given) =>
        NormalizePinyin(expected) == NormalizePinyin(given) && NormalizePinyin(given).Length > 0;

    /// <summary>
    /// True when the syllables are right but a tone is not — `shi2` for `shi4`. Scored as wrong
    /// either way, but saying which mistake it was is the most useful correction in Chinese.
    /// </summary>
    public static bool IsToneOnlyMistake(string expected, string given)
    {
        var want = NormalizePinyin(expected);
        var got = NormalizePinyin(given);

        if (want.Length == 0 || got.Length == 0 || want == got) return false;

        return ToneDigits().Replace(want, "") == ToneDigits().Replace(got, "");
    }

    /// <summary>Any single meaning the entry lists counts as the answer.</summary>
    public static bool EnglishMatches(string expectedField, string given)
    {
        var got = NormalizeEnglish(given);
        if (got.Length == 0) return false;

        return Alternatives(expectedField).Contains(got);
    }

    /// <summary>
    /// Every form of the entry a person could reasonably type. CC-CEDICT separates senses with
    /// `/` and synonyms within a sense with `;`, so "big; large; great / older (than another
    /// person)" offers four answers, not two — demanding the whole run "big large great" would
    /// be testing transcription rather than knowledge.
    /// A parenthesised aside is also optional: "older" answers "older (than another person)".
    /// </summary>
    private static HashSet<string> Alternatives(string expectedField)
    {
        var forms = new HashSet<string>();

        foreach (var sense in expectedField.Split(
                     ['/', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Add(forms, sense);
            Add(forms, Parenthetical().Replace(sense, " "));   // the same sense minus its aside
        }

        return forms;
    }

    private static void Add(HashSet<string> forms, string candidate)
    {
        var normalized = NormalizeEnglish(candidate);
        if (normalized.Length > 0) forms.Add(normalized);
    }

    /// <summary>Lowercase, ü as v, and all spacing dropped so `ni3 hao3` and `ni3hao3` agree.</summary>
    private static string NormalizePinyin(string value) =>
        Whitespace().Replace(value.Trim().ToLowerInvariant().Replace("u:", "v"), "");

    /// <summary>
    /// Case, surrounding filler and punctuation carry no meaning here: `to eat`, `Eat` and
    /// `the eat.` all reduce to `eat`.
    /// </summary>
    private static string NormalizeEnglish(string value)
    {
        var text = Punctuation().Replace(value.Trim().ToLowerInvariant(), " ");
        text = LeadingFiller().Replace(text, "");
        text = Articles().Replace(text, " ");

        return Whitespace().Replace(text, " ").Trim();
    }
}
