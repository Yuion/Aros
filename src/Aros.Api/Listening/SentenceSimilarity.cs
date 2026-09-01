using System.Text;

namespace Aros.Api.Listening;

public static class SentenceSimilarity
{
    /// <summary>
    /// Sørensen–Dice over character multisets: 1.0 for identical character content, 0.0 for none
    /// shared. Because the denominator is the combined length, it already rewards sentences of a
    /// similar size — a short sentence can never score highly against a long one.
    /// </summary>
    public static double Score(string a, string b)
    {
        var countsA = Counts(a);
        var countsB = Counts(b);

        var total = countsA.Values.Sum() + countsB.Values.Sum();
        if (total == 0) return 0;

        var shared = countsA
            .Where(pair => countsB.ContainsKey(pair.Key))
            .Sum(pair => Math.Min(pair.Value, countsB[pair.Key]));

        return 2.0 * shared / total;
    }

    private static Dictionary<Rune, int> Counts(string text)
    {
        var counts = new Dictionary<Rune, int>();
        foreach (var rune in text.EnumerateRunes())
            counts[rune] = counts.GetValueOrDefault(rune) + 1;

        return counts;
    }
}
