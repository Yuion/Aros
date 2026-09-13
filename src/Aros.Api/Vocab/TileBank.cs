using Aros.Api.Data.Entities;

namespace Aros.Api.Vocab;

/// <summary>
/// The characters offered when a question asks you to produce one.
///
/// Typing Chinese needs an IME, so the two character directions were multiple choice — and four
/// finished words side by side is a spotting exercise. Tiles ask for the same thing a keyboard
/// would: which characters, and in what order. A two-character word cannot be got right by
/// recognising one of them, and the wrong tiles are drawn from words that would actually fool you
/// rather than from whatever happened to be nearby.
/// </summary>
public static class TileBank
{
    /// <summary>Spare tiles beyond the answer's own, so the length of the answer gives nothing away.</summary>
    private const int Extras = 3;

    private const int Fewest = 5;
    private const int Most = 9;

    public static List<string> Build(VocabWord word, IEnumerable<VocabWord> pool)
    {
        var answer = Characters(word.Characters);
        var tiles = new List<string>(answer);
        var seen = new HashSet<string>(answer);

        var wanted = Math.Clamp(answer.Count + Extras, Fewest, Most);

        foreach (var candidate in Confusables.Rank(word, pool))
        {
            foreach (var character in Characters(candidate.Characters))
            {
                if (tiles.Count >= wanted) break;

                // A character the answer already uses is not a distractor, it is a second copy of
                // a right tile — and one that would make the same word spellable two ways
                if (seen.Add(character)) tiles.Add(character);
            }

            if (tiles.Count >= wanted) break;
        }

        return [.. tiles.OrderBy(_ => Random.Shared.Next())];
    }

    /// <summary>Characters as text elements, so a surrogate pair stays one tile.</summary>
    public static List<string> Characters(string text) =>
        [.. text.EnumerateRunes().Select(r => r.ToString())];
}
