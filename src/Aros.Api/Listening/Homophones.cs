using System.Text;
using Aros.Api.Data.Entities;

namespace Aros.Api.Listening;

/// <summary>
/// Collapses characters that sound alike onto a single representative, so two sentences can be
/// compared by how they *sound* rather than how they read.
/// </summary>
public static class Homophones
{
    /// <summary>Maps every character in a group to that group's first character.</summary>
    public static Dictionary<Rune, Rune> BuildMap(IEnumerable<HomophoneGroup> groups)
    {
        var map = new Dictionary<Rune, Rune>();

        foreach (var group in groups)
        {
            var runes = Runes(group.Characters);
            if (runes.Count < 2) continue;

            var representative = runes[0];
            foreach (var rune in runes)
                map.TryAdd(rune, representative);   // first group wins if a character was entered twice
        }

        return map;
    }

    /// <summary>
    /// The sentence rewritten with every sound-alike character replaced by its representative.
    /// Two sentences sharing an audible form cannot be told apart by listening.
    /// </summary>
    public static string AudibleForm(string sentence, Dictionary<Rune, Rune> map)
    {
        if (map.Count == 0) return sentence;

        var sb = new StringBuilder(sentence.Length);
        foreach (var rune in sentence.EnumerateRunes())
            sb.Append(map.TryGetValue(rune, out var representative) ? representative : rune);

        return sb.ToString();
    }

    public static List<Rune> Runes(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.EnumerateRunes().ToList();
}
