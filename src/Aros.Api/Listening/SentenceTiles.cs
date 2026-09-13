using Aros.Api.Data.Entities;
using Aros.Api.Vocab;

namespace Aros.Api.Listening;

/// <summary>
/// The characters offered when you have to rebuild a sentence you have only heard.
///
/// Writing out a sentence needs an IME; picking it out of three asks you to recognise it. Tiles ask
/// for what is actually in between: which characters were said, and in what order. Word order is
/// where Chinese hurts a beginner, and this is the only question in either trainer that tests it.
///
/// Punctuation is left out of the tiles entirely — it is not audible, so asking for it would test
/// nothing — and ignored when the answer is judged.
/// </summary>
public static class SentenceTiles
{
    /// <summary>Wrong tiles beyond the sentence's own.</summary>
    private const int Extras = 4;

    public static List<string> Build(TtsClip clip, IReadOnlyList<TtsClip> pool, Dictionary<string, string> homophones)
    {
        var answer = Characters(clip.Sentence);
        var tiles = new List<string>(answer);
        var seen = new HashSet<string>(answer);

        foreach (var character in Distractors(clip, pool, homophones))
        {
            if (tiles.Count >= answer.Count + Extras) break;

            // A character the sentence already uses is not a distractor: it would make the same
            // sentence spellable two ways and mark one of them wrong
            if (seen.Add(character)) tiles.Add(character);
        }

        return [.. tiles.OrderBy(_ => Random.Shared.Next())];
    }

    /// <summary>
    /// Wrong characters worth offering, best first: one that sounds like a character in the
    /// sentence, then characters from the sentences nearest this one. A homophone is the sharpest
    /// of these — it is the mistake the ear actually makes, and the whole reason the sound-alike
    /// groups exist.
    /// </summary>
    private static IEnumerable<string> Distractors(
        TtsClip clip, IReadOnlyList<TtsClip> pool, Dictionary<string, string> homophones)
    {
        var answer = Characters(clip.Sentence);

        foreach (var character in answer.OrderBy(_ => Random.Shared.Next()))
        {
            if (!homophones.TryGetValue(character, out var group)) continue;

            foreach (var alternative in Characters(group).Where(a => a != character))
                yield return alternative;
        }

        var nearest = pool
            .Where(c => c.Id != clip.Id)
            .OrderBy(c => SentenceSimilarity.Distance(clip.Sentence, c.Sentence))
            .ThenBy(_ => Random.Shared.Next());

        foreach (var other in nearest)
            foreach (var character in Characters(other.Sentence))
                yield return character;
    }

    /// <summary>The audible characters: Han only, so punctuation never becomes a tile.</summary>
    public static List<string> Characters(string sentence) =>
        [.. TileBank.Characters(sentence).Where(IsHan)];

    private static bool IsHan(string character) =>
        character.EnumerateRunes().All(r => r.Value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF);

    /// <summary>
    /// Judged on the characters alone. Punctuation is not audible and never offered, so a sentence
    /// is right when its characters are right and in the right order.
    /// </summary>
    public static bool Matches(string sentence, string given) =>
        string.Concat(Characters(sentence)) == string.Concat(Characters(given ?? ""));

    /// <summary>Right characters, wrong order — worth saying, since it is a different mistake.</summary>
    public static bool SameCharacters(string sentence, string given) =>
        Characters(sentence).OrderBy(c => c, StringComparer.Ordinal)
            .SequenceEqual(Characters(given ?? "").OrderBy(c => c, StringComparer.Ordinal));
}
