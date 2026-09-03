using System.Text.RegularExpressions;

namespace Aros.Api.Tts;

public record DumpRow(string Sentence, string Pinyin, string English);

/// <summary>
/// Reads a pasted batch of sentences — the markdown table a chat assistant produces:
///
/// <code>
/// | Chinese | Pinyin             | Meaning     |
/// | ------- | ------------------ | ----------- |
/// | 我喜欢茶。   | wo3 xi3 huan5 cha2 | I like tea. |
/// </code>
///
/// Rows are found by content rather than by position: the cell holding Han characters is the
/// sentence and the tone-numbered cell is the pinyin, whichever columns they arrive in. Headers,
/// rule lines and anything without Chinese in it are skipped, so pasting the surrounding chat
/// text along with the table costs nothing.
/// </summary>
public static partial class SentenceDump
{
    [GeneratedRegex(@"^:?-{2,}:?$")]
    private static partial Regex RuleCell();

    /// <summary>Space-separated syllables carrying tone numbers — `wo3 xi3 huan5 cha2`.</summary>
    [GeneratedRegex(@"^[a-zA-ZüÜ:]+[1-5](\s+[a-zA-ZüÜ:]+[1-5])*$")]
    private static partial Regex TonedPinyin();

    public static List<DumpRow> Parse(string? text)
    {
        var rows = new List<DumpRow>();
        if (text is null) return rows;

        var seen = new HashSet<string>();

        foreach (var line in text.Split('\n'))
        {
            var cells = Cells(line);
            if (cells.Count < 2) continue;

            var sentence = cells.FirstOrDefault(HasHan);
            if (sentence is null) continue;                 // header, rule, or prose around the table

            var rest = cells.Where(c => c != sentence).ToList();
            var pinyin = rest.FirstOrDefault(c => TonedPinyin().IsMatch(c)) ?? "";
            var english = rest.FirstOrDefault(c => c != pinyin && c.Length > 0) ?? "";

            if (seen.Add(sentence)) rows.Add(new DumpRow(sentence, pinyin, english));
        }

        return rows;
    }

    private static List<string> Cells(string line)
    {
        var trimmed = line.Trim().Trim('|');
        if (trimmed.Length == 0) return [];

        var separator = trimmed.Contains('|') ? '|' : '\t';

        return [.. trimmed
            .Split(separator)
            .Select(c => c.Trim())
            .Where(c => c.Length > 0 && !RuleCell().IsMatch(c))];
    }

    /// <summary>Any CJK ideograph — the same ranges the vocabulary segmenter scans.</summary>
    private static bool HasHan(string value) =>
        value.Any(c => c is >= (char)0x4E00 and <= (char)0x9FFF
                        or >= (char)0x3400 and <= (char)0x4DBF
                        or >= (char)0xF900 and <= (char)0xFAFF);
}
