using System.Text;
using System.Text.RegularExpressions;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Vocab;

public record HarvestResult(int Added, int NeedsReview, List<string> Words);

/// <summary>
/// Grows the vocabulary pool out of the sentences in the TTS library. A sentence is segmented
/// against the dictionary and any word not already held is added, so the pool is exactly the
/// vocabulary being practised — and nothing has to be typed by hand.
/// </summary>
public partial class VocabHarvester(AppDbContext db, ILogger<VocabHarvester> logger)
{
    /// <summary>Longest word CC-CEDICT is worth probing for at each position.</summary>
    private const int MaxWordLength = 8;

    public async Task<HarvestResult> HarvestAsync(string sentence, CancellationToken ct)
    {
        var segments = await SegmentAsync(sentence, ct);
        if (segments.Count == 0) return new HarvestResult(0, 0, []);

        var added = new List<VocabWord>();

        foreach (var segment in segments.Distinct())
        {
            if (await BuildAsync(segment, ct) is { } word) added.Add(word);
        }

        if (added.Count > 0)
        {
            db.VocabWords.AddRange(added);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Harvested {Count} new words from \"{Sentence}\"", added.Count, sentence);
        }

        return new HarvestResult(
            added.Count,
            added.Count(w => w.NeedsReview),
            added.Select(w => w.Characters).ToList());
    }

    /// <summary>
    /// Adds one word by hand, without needing a sentence to find it in. The characters are taken
    /// as given — a deliberate 中国人 stays one entry rather than being segmented apart — but the
    /// dictionary lookup, the pick between readings and the review flag all behave exactly as they
    /// do during a harvest.
    /// </summary>
    public async Task<VocabWord> AddAsync(string? characters, CancellationToken ct)
    {
        var cleaned = new string(
            (characters ?? "").EnumerateRunes().Where(IsHan).SelectMany(r => r.ToString()).ToArray());

        if (cleaned.Length == 0)
            throw new VocabException("Enter one or more Chinese characters.");

        var word = await BuildAsync(cleaned, ct);

        if (word is null)
        {
            var existing = await db.VocabWords
                .Where(w => w.Characters == cleaned)
                .Select(w => w.Pinyin)
                .FirstOrDefaultAsync(ct);

            throw new VocabException($"{cleaned} is already in your vocabulary ({existing}).");
        }

        db.VocabWords.Add(word);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Added {Characters} by hand", cleaned);

        return word;
    }

    /// <summary>
    /// Looks a headword up and shapes the entry. Returns null when it is already held, so both
    /// callers can treat "nothing new" the same way.
    /// </summary>
    private async Task<VocabWord?> BuildAsync(string characters, CancellationToken ct)
    {
        var readings = await db.DictionaryEntries
            .Where(d => d.Simplified == characters)
            .AsNoTracking()
            .ToListAsync(ct);

        // Dictionary order is alphabetical by pinyin, not by how common a sense is, so the
        // first row is a poor default — for 水 it is the surname Shui, not water.
        readings = [.. readings.OrderBy(SensePriority).ThenBy(d => d.Id)];

        var chosen = readings.FirstOrDefault();

        // With several readings there is nothing to disambiguate against, so take the best guess
        // and flag it. Anything flagged stays out of tests until confirmed — see VocabService.
        var needsReview = readings.Count != 1;

        var pinyin = chosen is null ? "" : CedictImporter.ForDisplay(chosen.Pinyin);
        var english = chosen is null ? "" : FirstSenses(chosen.English);

        var exists = await db.VocabWords
            .AnyAsync(w => w.Characters == characters && w.Pinyin == pinyin, ct);
        if (exists) return null;

        return new VocabWord
        {
            Characters = characters,
            Pinyin = pinyin,
            English = english,
            NeedsReview = needsReview,
            ReadingAlternatives = readings.Count > 1
                ? string.Join(" | ", readings.Skip(1).Take(5).Select(r => $"{r.Pinyin} — {FirstSenses(r.English)}"))
                : null,
        };
    }

    /// <summary>
    /// Greedy longest-match against the dictionary: at each position take the longest run of
    /// characters that is a known headword, so 我是中国人 yields 我 / 是 / 中国 / 人 rather than
    /// five loose characters. An unknown character becomes a segment of its own — it is still
    /// worth learning, it just arrives flagged for review.
    /// </summary>
    public async Task<List<string>> SegmentAsync(string sentence, CancellationToken ct)
    {
        var runes = sentence.EnumerateRunes().Where(IsHan).ToList();
        var segments = new List<string>();
        var position = 0;

        while (position < runes.Count)
        {
            var take = Math.Min(MaxWordLength, runes.Count - position);
            var matched = false;

            for (; take > 1; take--)
            {
                var candidate = Join(runes, position, take);
                if (await db.DictionaryEntries.AnyAsync(d => d.Simplified == candidate, ct))
                {
                    segments.Add(candidate);
                    position += take;
                    matched = true;
                    break;
                }
            }

            if (matched) continue;

            segments.Add(Join(runes, position, 1));
            position++;
        }

        return segments;
    }

    /// <summary>
    /// Lower sorts first. Proper nouns and pointer entries ("variant of", "abbr. for",
    /// "used in names") are real dictionary rows but almost never the sense a learner wants,
    /// so they yield to any ordinary reading of the same characters.
    /// </summary>
    private static int SensePriority(DictionaryEntry entry)
    {
        var score = 0;

        // CC-CEDICT capitalises the pinyin of names and places
        if (entry.Pinyin.Length > 0 && char.IsUpper(entry.Pinyin[0])) score += 2;

        if (IsPointerSense(entry.English)) score += 1;

        return score;
    }

    private static bool IsPointerSense(string english)
    {
        string[] markers = ["variant of", "abbr. for", "used in ", "old variant", "surname ", "see "];
        var first = english.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .FirstOrDefault() ?? "";

        return markers.Any(m => first.StartsWith(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A short gloss, because this doubles as the question when testing English → something.
    /// CC-CEDICT entries carry classifier lines, usage notes and parenthesised grammar labels
    /// alongside the actual meaning; a prompt reading
    /// "(third-person singular) (since the early 20th century)…" is unusable, so plain senses
    /// are preferred and only the leading label is stripped when there is nothing else.
    /// </summary>
    private static string FirstSenses(string english, int count = 2)
    {
        var senses = english
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !s.StartsWith("CL:", StringComparison.Ordinal))     // classifiers aren't a meaning
            .Select(CleanReferences)
            .Where(s => s.Length > 0)
            .ToList();

        var plain = senses.Where(s => !s.StartsWith('(')).Take(count).ToList();
        if (plain.Count > 0) return string.Join(" / ", plain);

        // Everything was parenthesised — drop the leading label, unless that empties the
        // sense entirely, as it does for particles whose whole definition is one aside.
        var fallback = senses.FirstOrDefault() ?? "";
        var stripped = LeadingLabel().Replace(fallback, "").Trim();

        return stripped.Length > 0 ? stripped : fallback;
    }

    /// <summary>
    /// CC-CEDICT cites other headwords inline as `書經|书经[Shu1 jing1]`. Keeping the simplified
    /// form and dropping the bracketed pinyin leaves a gloss that reads as English.
    /// </summary>
    private static string CleanReferences(string sense)
    {
        var text = TraditionalSimplifiedPair().Replace(sense, "$1");
        text = BracketedReading().Replace(text, "");

        return Spaces().Replace(text, " ").Replace(" ,", ",").Trim();
    }

    [GeneratedRegex(@"^\([^)]*\)\s*")]
    private static partial Regex LeadingLabel();

    [GeneratedRegex(@"[一-鿿]+\|([一-鿿]+)")]
    private static partial Regex TraditionalSimplifiedPair();

    [GeneratedRegex(@"\s*\[[^\]]*\]")]
    private static partial Regex BracketedReading();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    private static string Join(List<Rune> runes, int start, int length)
    {
        var sb = new StringBuilder(length * 2);
        for (var i = start; i < start + length; i++) sb.Append(runes[i]);
        return sb.ToString();
    }

    /// <summary>CJK ideographs only — punctuation, latin and digits are not vocabulary.</summary>
    private static bool IsHan(Rune rune) =>
        rune.Value is >= 0x4E00 and <= 0x9FFF        // CJK Unified Ideographs
                   or >= 0x3400 and <= 0x4DBF        // Extension A
                   or >= 0xF900 and <= 0xFAFF;       // Compatibility Ideographs
}
