using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Text;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Vocab;

/// <summary>A row the import would not apply, and why.</summary>
public record VocabConflict(string Characters, string Pinyin, string HeldPinyin);

public record VocabImportResult(
    int Parsed,
    int Added,
    int Updated,
    int Unchanged,
    int Skipped,
    IReadOnlyList<VocabConflict> Conflicts);

/// <summary>
/// Takes vocabulary from a pasted table, the same shape the sentences use. Words arrive already
/// judged — you wrote or checked the reading yourself — so nothing imported here waits in the
/// review queue.
///
/// Matching is on characters **and** pinyin, so 行/xing2 and 行/hang2 stay separate, separately
/// scored entries. A row whose characters are held under a different reading is reported rather
/// than applied: it is either a second reading you did not mean to add or a correction to one you
/// did, and guessing between those would either lose an entry's history or invent a word.
/// </summary>
public class VocabImporter(AppDbContext db)
{
    public Task<VocabImportResult> PreviewAsync(string? text, CancellationToken ct) =>
        RunAsync(text, apply: false, needsReview: false, ct);

    /// <param name="needsReview">
    /// False when you typed the table out — writing it is the review. True for anything a model
    /// produced: a plausible-looking wrong tone is exactly what it gets wrong, and once drilled in
    /// it is learned wrong. The queue already exists and already holds flagged words out of tests.
    /// </param>
    public Task<VocabImportResult> ImportAsync(string? text, CancellationToken ct, bool needsReview = false) =>
        RunAsync(text, apply: true, needsReview, ct);

    private async Task<VocabImportResult> RunAsync(string? text, bool apply, bool needsReview, CancellationToken ct)
    {
        var rows = TableDump.Parse(text);

        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var skipped = 0;
        var conflicts = new List<VocabConflict>();

        var held = await db.VocabWords.ToListAsync(ct);
        var byCharacters = held.GroupBy(w => w.Characters).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var row in rows)
        {
            var characters = row.Chinese.Trim();
            var pinyin = Normalize(row.Pinyin);
            var english = row.English.Trim();

            if (characters.Length == 0 || pinyin.Length == 0)
            {
                skipped++;                       // nothing to key on, or nothing to teach
                continue;
            }

            if (!byCharacters.TryGetValue(characters, out var existing))
            {
                added++;
                if (apply) Add(byCharacters, characters, pinyin, english, needsReview);
                continue;
            }

            if (existing.FirstOrDefault(w => Normalize(w.Pinyin) == pinyin) is { } match)
            {
                if (match.English == english && match.NeedsReview == needsReview)
                {
                    unchanged++;
                    continue;
                }

                updated++;
                if (apply)
                {
                    match.Pinyin = pinyin;
                    match.English = english;
                    match.NeedsReview = needsReview;
                    match.ReadingAlternatives = null;
                }

                continue;
            }

            conflicts.Add(new VocabConflict(characters, pinyin, string.Join(" / ", existing.Select(w => w.Pinyin))));
        }

        if (apply) await db.SaveChangesAsync(ct);

        return new VocabImportResult(rows.Count, added, updated, unchanged, skipped, conflicts);
    }

    private void Add(
        Dictionary<string, List<VocabWord>> index, string characters, string pinyin, string english, bool needsReview)
    {
        var word = new VocabWord
        {
            Characters = characters,
            Pinyin = pinyin,
            English = english,
            NeedsReview = needsReview,
        };

        db.VocabWords.Add(word);

        // Keep the index current, so the same word twice in one paste is not inserted twice
        if (!index.TryGetValue(characters, out var list)) index[characters] = list = [];
        list.Add(word);
    }

    /// <summary>Same rules the trainer marks answers by: lowercase, ü as v, single spaces.</summary>
    private static string Normalize(string pinyin) => Pinyin.ForDisplay(pinyin);
}
