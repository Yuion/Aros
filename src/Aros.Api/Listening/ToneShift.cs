using Aros.Api.Data.Entities;

namespace Aros.Api.Listening;

/// <summary>
/// Whether a sentence is actually read differently from the way its words are listed.
///
/// Writing the pinyin of a sentence whose every syllable is the dictionary form is a transcription
/// exercise: the vocabulary trainer already asks every one of those words for its reading, and the
/// sentence adds nothing but typing. What it can test that nothing else does is sandhi — 不 said
/// bu2 before a fourth tone, 一 as yi4 or yi2, a third tone before another third tone — because
/// that is a fact about the sentence rather than about any word in it.
///
/// Comparison is by syllable rather than by position. Lining a reading up with its characters
/// needs a segmenter and would be wrong at the first two-syllable word; a syllable that appears in
/// the sentence with a tone no listed word gives it, while the listed form is nowhere in the
/// sentence, is a shift with no false positives worth the machinery.
/// </summary>
public static class ToneShift
{
    /// <summary>The words that could shift, as characters and dictionary reading.</summary>
    public record Entry(string Characters, string Pinyin);

    public static bool Present(TtsClip clip, IReadOnlyList<Entry> vocabulary)
    {
        if (clip.Pinyin.Length == 0) return false;

        var spoken = Syllables(clip.Pinyin);
        if (spoken.Count == 0) return false;

        var asSaid = spoken.ToHashSet();

        foreach (var word in vocabulary)
        {
            if (word.Characters.Length == 0 || !clip.Sentence.Contains(word.Characters)) continue;

            foreach (var listed in Syllables(word.Pinyin))
            {
                if (Tone(listed) == 0) continue;

                // The sentence says it the listed way somewhere, so nothing was moved
                if (asSaid.Contains(listed)) continue;

                if (spoken.Any(said => Bare(said) == Bare(listed) && Tone(said) != 0 && Tone(said) != Tone(listed)))
                    return true;
            }
        }

        return false;
    }

    private static List<string> Syllables(string pinyin) =>
        [.. pinyin.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    private static string Bare(string syllable) => syllable.TrimEnd('0', '1', '2', '3', '4', '5');

    private static int Tone(string syllable) =>
        syllable.Length > 0 && char.IsDigit(syllable[^1]) ? syllable[^1] - '0' : 0;
}
