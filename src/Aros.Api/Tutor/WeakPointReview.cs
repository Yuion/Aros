using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

/// <summary>
/// Closes weaknesses the trainers have since disproved, and folds duplicates together.
///
/// The tutor records a weakness when it notices one and nothing ever takes it back: nine were
/// open, none had ever been resolved, the oldest was three weeks old, and every one had
/// FirstSeen equal to LastSeen — recorded once, never seen again, never cleared. They sat in the
/// tutor's context for weeks steering lessons towards problems that were gone.
///
/// A weakness naming Chinese characters can be checked: the trainers have been asking about
/// those very characters ever since. If every answer touching them since the weakness was last
/// seen has been right, and there have been enough of them to mean something, it is fixed. A
/// weakness naming no characters — "sentence-level tone recall" — cannot be checked this way and
/// is left alone for you to close by hand.
/// </summary>
public class WeakPointReview(AppDbContext db, ILogger<WeakPointReview> log)
{
    /// <summary>
    /// How many answers about the characters must have gone by. Two or three right answers is a
    /// good day, not evidence; this is about a fortnight of the item coming round.
    /// </summary>
    private const int EnoughAnswers = 6;

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var open = await db.WeakPoints.Where(w => !w.Resolved).ToListAsync(ct);
        if (open.Count == 0) return 0;

        var closed = Merge(open);

        // Read once, matched in memory. Which words contain a given character is not something
        // the database can be asked in one translatable expression, and the whole vocabulary is
        // a few dozen rows.
        var words = await db.VocabWords
            .AsNoTracking()
            .Select(w => new { w.Id, w.Characters })
            .ToListAsync(ct);

        foreach (var weak in open.Where(w => !w.Resolved))
        {
            var characters = weak.Target.Where(IsHan).Distinct().ToList();
            if (characters.Count == 0) continue;

            var touched = words
                .Where(w => characters.Any(c => w.Characters.Contains(c)))
                .Select(w => w.Id)
                .ToList();

            if (touched.Count > 0 && await SettledAsync(weak, touched, ct))
                Close(weak, ref closed);
        }

        if (closed > 0) await db.SaveChangesAsync(ct);

        return closed;
    }

    private void Close(WeakPoint weak, ref int closed)
    {
        weak.Resolved = true;
        weak.ResolvedAt = DateTime.UtcNow;
        closed++;

        log.LogInformation("Weak point resolved by the trainers: {Target}", weak.Target);
    }

    /// <summary>
    /// "不坐 versus 不想坐" and "不坐 vs 不想坐" are one weakness written twice, and both were
    /// open. Two targets that say the same thing once the wording is stripped out are merged
    /// into the older row, which keeps the date the problem actually started.
    /// </summary>
    private int Merge(List<WeakPoint> open)
    {
        var merged = 0;

        foreach (var group in open.GroupBy(w => Key(w.Target)).Where(g => g.Count() > 1))
        {
            var kept = group.OrderBy(w => w.FirstSeen).First();

            foreach (var duplicate in group.Where(w => w.Id != kept.Id))
            {
                kept.Severity = Math.Max(kept.Severity, duplicate.Severity);
                kept.LastSeen = kept.LastSeen > duplicate.LastSeen ? kept.LastSeen : duplicate.LastSeen;

                duplicate.Resolved = true;
                duplicate.ResolvedAt = DateTime.UtcNow;
                merged++;
            }
        }

        return merged;
    }

    private static bool IsHan(char c) => c is >= (char)0x4E00 and <= (char)0x9FFF;

    /// <summary>The characters and letters of a target, with the joining words thrown away.</summary>
    private static string Key(string target)
    {
        var words = target
            .ToLowerInvariant()
            .Split([' ', '/', ',', '·', '(', ')', '—', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("versus" or "vs" or "vs." or "and" or "or" or "the" or "a"))
            .OrderBy(w => w, StringComparer.Ordinal);

        return string.Join(' ', words);
    }

    /// <summary>
    /// Whether the trainers have settled this one: enough answers about the characters it names,
    /// since it was last seen, and none of them wrong.
    /// </summary>
    private async Task<bool> SettledAsync(WeakPoint weak, List<int> words, CancellationToken ct)
    {
        var since = weak.LastSeen;

        // A weakness about tones is only disproved by answers that had to get the tones right.
        // Recognising what 一 means says nothing about whether you still say yi1 before a fourth
        // tone, so those are settled by the reading direction alone.
        var aboutTones = $"{weak.Target} {weak.Type}".Contains("tone", StringComparison.OrdinalIgnoreCase);

        var answers = await db.VocabAnswers
            .AsNoTracking()
            .Where(a => words.Contains(a.VocabWordId) && a.AnsweredAt > since)
            .Where(a => !aboutTones || a.Direction == VocabDirection.CharactersToPinyin)
            .Select(a => a.Correct)
            .ToListAsync(ct);

        return answers.Count >= EnoughAnswers && answers.All(correct => correct);
    }
}
