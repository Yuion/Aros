using System.Text;

namespace Aros.Api.Listening;

public static class SentenceSimilarity
{
    /// <summary>
    /// Levenshtein distance in characters: how many single-character edits turn one sentence into
    /// the other. 1 means the pair differs by exactly one character — the hardest possible choice,
    /// and what distractor selection aims for.
    /// </summary>
    public static int Distance(string a, string b)
    {
        var left = Runes(a);
        var right = Runes(b);

        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        // Two rolling rows rather than the full matrix — sentences are short, but this keeps it flat.
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++) previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                var deletion = previous[j] + 1;
                var insertion = current[j - 1] + 1;

                current[j] = Math.Min(substitution, Math.Min(deletion, insertion));
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static Rune[] Runes(string text) => text.EnumerateRunes().ToArray();
}
