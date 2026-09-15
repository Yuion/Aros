using Aros.Api.Data.Entities;

namespace Aros.Api.Tutor;

/// <summary>
/// A reading for a line of Chinese, assembled from the words already in the library.
///
/// An exercise whose task is written in characters — "他坐车去机场。 turn it into a question" — is
/// unreadable to someone who does not read characters yet, which is the person the tutor is
/// teaching. The tutor is asked for the reading with the prompt; this is what fills the gap when it
/// forgets, and it can only use what the learner has already been taught, which is exactly the
/// vocabulary that a prompt is supposed to be built from.
///
/// Longest match wins, so 机场 reads as one word rather than as two characters. Anything the
/// library does not know is left out and reported, because a reading with a hole in it is still
/// worth more than none — and the hole says which character to look up.
/// </summary>
public static class Readings
{
    /// <summary>The longest word worth trying to match, in characters.</summary>
    private const int LongestWord = 4;

    /// <summary>
    /// Characters → reading, from every word the learner has been taught, plus the single
    /// characters those words are made of.
    ///
    /// The readings are stored one syllable per character and separated by spaces, so 坐车 =
    /// "zuo4 che1" gives 坐 = zuo4 and 车 = che1 by counting. That matters because a prompt can
    /// use a character outside the word it was learned in — a scrambled sentence in an
    /// error-correction task does exactly that — and a word's own entry wins over anything
    /// derived from a longer one.
    /// </summary>
    public static Dictionary<string, string> Lookup(IEnumerable<VocabWord> words)
    {
        var lookup = new Dictionary<string, string>();
        var derived = new Dictionary<string, string>();

        foreach (var word in words)
        {
            var characters = (word.Characters ?? "").Trim();
            var pinyin = (word.Pinyin ?? "").Trim();

            if (characters.Length == 0 || pinyin.Length == 0) continue;

            // First one wins: the vocabulary list is the authority, and a later duplicate is a
            // second sense of the same word rather than a second reading
            lookup.TryAdd(characters, pinyin);

            var runes = SplitRunes(characters);
            var syllables = pinyin.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Only when they line up: a reading that does not is not safe to cut apart
            if (runes.Count != syllables.Length) continue;

            for (var i = 0; i < runes.Count; i++)
                if (IsHan(runes[i]))
                    derived.TryAdd(runes[i], syllables[i]);
        }

        foreach (var (character, reading) in derived)
            lookup.TryAdd(character, reading);

        return lookup;
    }

    /// <summary>
    /// The reading, and whatever could not be read.
    ///
    /// A character with no reading is kept in the line as itself rather than dropped: dropping it
    /// silently turns 你怎么去北京？into "ni3 qu4 bei3 jing1", which is a different question and
    /// reads as though it were the whole sentence. Left in, the gap is visible and the rest still
    /// helps. An empty result means nothing at all was recognised, and then it is better to say
    /// nothing.
    /// </summary>
    public static (string Reading, List<string> Unknown) For(string? text, IReadOnlyDictionary<string, string> lookup)
    {
        var characters = SplitRunes(text ?? "");
        var parts = new List<string>();
        var unknown = new List<string>();
        var index = 0;

        while (index < characters.Count)
        {
            if (!IsHan(characters[index]))
            {
                index++;                         // punctuation carries no sound
                continue;
            }

            var matched = false;

            for (var length = Math.Min(LongestWord, characters.Count - index); length > 0; length--)
            {
                var candidate = string.Concat(characters.Skip(index).Take(length));

                if (!lookup.TryGetValue(candidate, out var reading)) continue;

                parts.Add(reading);
                index += length;
                matched = true;
                break;
            }

            if (matched) continue;

            parts.Add(characters[index]);        // no reading: the character stands for itself
            unknown.Add(characters[index]);
            index++;
        }

        // Nothing but unread characters is not a reading, it is the prompt again
        var readable = parts.Count > unknown.Count;

        return (readable ? string.Join(" ", parts) : "", unknown);
    }

    /// <summary>Whether a line needs a reading at all: only Chinese does.</summary>
    public static bool HasHan(string? text) =>
        SplitRunes(text ?? "").Any(IsHan);

    private static List<string> SplitRunes(string text) =>
        [.. text.EnumerateRunes().Select(r => r.ToString())];

    private static bool IsHan(string character) =>
        character.EnumerateRunes().All(r => r.Value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF);
}
