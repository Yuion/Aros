using Aros.Api.Syllabus;

namespace Aros.Api.Tones;

/// <summary>One syllable said in one tone, and a character that says it.</summary>
public record ToneSound(string Syllable, int Tone, string Character, string Pinyin);

/// <summary>
/// The sounds the tone trainer draws on: every syllable that exists in all four tones, with one
/// character for each.
///
/// Built from the syllabus file rather than kept as its own list, so there is one place where
/// Chinese words and their readings live. Three rules decide what gets in:
///
/// - all four tones, or the question "which tone was that" has a missing answer for that syllable
/// - one character per reading, since 好 is both hǎo and hào and the voice would not be saying
///   the tone the question claims
/// - single characters only, so what is heard is one syllable and nothing else
///
/// A character being obscure does not matter and is not a problem to solve: 癌 and 坝 are here to
/// be heard, not read, and the character is hidden until the answer is given.
/// </summary>
public static class ToneBank
{
    public static IReadOnlyList<ToneSound> Sounds { get; } = Build();

    public static ToneSound? Find(string syllable, int tone) =>
        Sounds.FirstOrDefault(s => s.Syllable == syllable && s.Tone == tone);

    private static List<ToneSound> Build()
    {
        var readings = new Dictionary<string, HashSet<string>>();
        var singles = new List<(string Character, string Pinyin)>();

        foreach (var word in HskFile.Rows())
        {
            if (word.Word.EnumerateRunes().Count() != 1) continue;
            if (word.Pinyin.Contains(' ')) continue;

            if (!readings.TryGetValue(word.Word, out var forms))
                readings[word.Word] = forms = [];

            forms.Add(word.Pinyin);
            singles.Add((word.Word, word.Pinyin));
        }

        // syllable -> tone -> the first character that says it
        var bySyllable = new Dictionary<string, Dictionary<int, (string Character, string Pinyin)>>();

        foreach (var (character, pinyin) in singles)
        {
            if (readings[character].Count > 1) continue;          // 好 is hǎo and hào; skip it

            var tone = ToneOf(pinyin);
            if (tone is < 1 or > 4) continue;                     // neutral is not a tone you can hear alone

            var syllable = Bare(pinyin);

            if (!bySyllable.TryGetValue(syllable, out var tones))
                bySyllable[syllable] = tones = [];

            tones.TryAdd(tone, (character, pinyin));
        }

        return
        [
            .. bySyllable
                .Where(pair => pair.Value.Count == 4)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SelectMany(pair => pair.Value
                    .OrderBy(t => t.Key)
                    .Select(t => new ToneSound(pair.Key, t.Key, t.Value.Character, t.Value.Pinyin)))
        ];
    }

    /// <summary>Which tone a pinyin syllable carries, read from its diacritic.</summary>
    public static int ToneOf(string pinyin)
    {
        foreach (var c in pinyin.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (c == '̄') return 1;        // ā
            if (c == '́') return 2;        // á
            if (c == '̌') return 3;        // ǎ
            if (c == '̀') return 4;        // à
        }

        return 5;
    }

    /// <summary>The syllable without its tone: cài and cāi are both "cai".</summary>
    public static string Bare(string pinyin) =>
        new string([.. pinyin.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => c is < '̀' or > 'ͯ')]).ToLowerInvariant();
}
