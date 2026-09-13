using System.Text.RegularExpressions;
using Aros.Api.Data.Entities;

namespace Aros.Api.Vocab;

/// <summary>
/// Which other words are worth putting in front of you when this one is asked.
///
/// The old rule picked the nearest words by edit distance on the characters. In a pool of mostly
/// single characters that is close to random: 水 next to 茶 next to 书 is a spotting exercise, not
/// a choice, which is why the two character directions ran at 100% while typed recall sat at 83%.
///
/// A useful wrong answer is confusable **along the axis being tested**, and four kinds are worth
/// having together: a word that sounds the same but for its tone, one that shares a character with
/// the answer, one that means something adjacent, and — since a pool this size rarely holds a true
/// tone twin — one that is a single sound away. Each catches a different way of half-knowing a
/// word, and the mix is deliberate: three tone neighbours would only ever test tones.
/// </summary>
public static partial class Confusables
{
    /// <summary>How close another word is to this one. Higher is more confusable.</summary>
    public enum Kind
    {
        None = 0,
        Near = 1,           // one sound away — 水 shui3 / 书 shu1
        Meaning = 2,        // adjacent meaning — tea / water
        Shared = 3,         // shares a character — 银行 / 行
        Sound = 4,          // same syllables, different tones — 想 xiang3 / 香 xiang1
    }

    [GeneratedRegex(@"[1-5]")]
    private static partial Regex ToneDigits();

    [GeneratedRegex(@"[^a-z ]")]
    private static partial Regex NotWord();

    /// <summary>
    /// The pool ordered by how well each word would fool you, one kind at a time so the result
    /// holds all three rather than three of whichever kind is commonest. Ties are shuffled: the
    /// same word must not draw the same three distractors every round.
    /// </summary>
    public static List<VocabWord> Rank(VocabWord word, IEnumerable<VocabWord> pool)
    {
        var others = pool.Where(w => w.Id != word.Id && w.Characters != word.Characters).ToList();

        var scored = others
            .Select(other => (Word: other, Kind: Compare(word, other)))
            .OrderByDescending(x => x.Kind)
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        // One of each kind first, then the rest — so a word with a sound-alike, a character in
        // common and a neighbouring meaning offers all three before doubling up on any
        var ranked = new List<VocabWord>();
        var taken = new HashSet<int>();

        foreach (var kind in new[] { Kind.Sound, Kind.Shared, Kind.Meaning, Kind.Near })
        {
            if (scored.FirstOrDefault(x => x.Kind == kind) is { Word: not null } best && taken.Add(best.Word.Id))
                ranked.Add(best.Word);
        }

        ranked.AddRange(scored.Where(x => taken.Add(x.Word.Id)).Select(x => x.Word));

        return ranked;
    }

    public static Kind Compare(VocabWord word, VocabWord other)
    {
        if (SoundsAlike(word.Pinyin, other.Pinyin)) return Kind.Sound;
        if (SharesCharacter(word.Characters, other.Characters)) return Kind.Shared;
        if (MeansSomethingNear(word.English, other.English)) return Kind.Meaning;
        if (SoundsClose(word.Pinyin, other.Pinyin)) return Kind.Near;

        return Kind.None;
    }

    /// <summary>Same syllables, different tones. The one difference a reading can have and still be wrong.</summary>
    private static bool SoundsAlike(string pinyin, string other)
    {
        if (pinyin.Length == 0 || other.Length == 0) return false;
        if (Toneless(pinyin) != Toneless(other)) return false;

        return !pinyin.Equals(other, StringComparison.OrdinalIgnoreCase);
    }

    private static string Toneless(string pinyin) =>
        ToneDigits().Replace(pinyin, "").Replace(" ", "").ToLowerInvariant();

    /// <summary>
    /// One sound away, tones ignored: 水 shui3 beside 书 shu1. A pool this size rarely holds a true
    /// tone twin for a given word, and a near-miss still asks you to hear the difference — which is
    /// more than a word picked for being nearby asks.
    /// </summary>
    private static bool SoundsClose(string pinyin, string other)
    {
        if (pinyin.Length == 0 || other.Length == 0) return false;

        var mine = Toneless(pinyin);
        var theirs = Toneless(other);

        // Only within a syllable of the same size: shui/shu is a slip, shui/zhongguo is not
        return Math.Abs(mine.Length - theirs.Length) <= 1
               && mine != theirs
               && Distance(mine, theirs) == 1;
    }

    /// <summary>Levenshtein, over the few letters of a reading.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// 银行 beside 行 is a real trap: the characters are right and the selection is not. It only
    /// counts when at least one of the two is longer, or every single character would match itself.
    /// </summary>
    private static bool SharesCharacter(string characters, string other)
    {
        if (characters.Length == 0 || other.Length == 0) return false;
        if (characters.Length == 1 && other.Length == 1) return false;

        return characters.Intersect(other).Any();
    }

    /// <summary>
    /// Meanings that sit next to each other. Compared on whole words, minus the ones every gloss
    /// contains — "to be" and "to go" share "to" and nothing else.
    /// </summary>
    private static bool MeansSomethingNear(string english, string other)
    {
        var mine = Words(english);

        return mine.Count > 0 && Words(other).Overlaps(mine);
    }

    private static readonly HashSet<string> Ignored =
        ["to", "a", "an", "the", "of", "or", "and", "for", "is", "be", "it", "in", "on", "at", "that", "this"];

    private static HashSet<string> Words(string english) =>
    [
        .. NotWord()
            .Replace(english.ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !Ignored.Contains(w))
    ];
}
