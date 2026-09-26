using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Writing;

/// <summary>How one word has gone in one mode.</summary>
public record ModeRecord(int Attempts, int Clean, int Mistakes, DateTime? LastAt);

public record WritingWord(
    int Id,
    string Characters,
    string Pinyin,
    string English,
    ModeRecord Copying,
    ModeRecord Memory);

/// <summary>
/// Handwriting practice, kept apart from everything that schedules.
///
/// Nothing in here reads or writes VocabProgress, DrawWeight, MissTally or RestSchedule, and
/// nothing outside reads WritingAttempts. That separation is the feature, not an oversight:
/// how well a character is drawn is a different question from whether the word is known, and a
/// bad afternoon at the tablet should not bring a word you can read and hear back into rotation.
/// </summary>
public class WritingService(AppDbContext db)
{
    /// <summary>
    /// Every word that can be written, with its record in each mode.
    ///
    /// Words still awaiting a reading check are left out for the same reason the trainers leave
    /// them out - practising a guess is worse than not practising.
    /// </summary>
    public async Task<IReadOnlyList<WritingWord>> WordsAsync(CancellationToken ct)
    {
        var words = await db.VocabWords
            .AsNoTracking()
            .Where(w => !w.NeedsReview)
            .OrderBy(w => w.Characters)
            .Select(w => new { w.Id, w.Characters, w.Pinyin, w.English })
            .ToListAsync(ct);

        var tallies = await db.WritingAttempts
            .AsNoTracking()
            .GroupBy(a => new { a.WordId, a.Mode })
            .Select(group => new
            {
                group.Key.WordId,
                group.Key.Mode,
                Attempts = group.Count(),
                Clean = group.Count(a => a.Mistakes == 0),
                Mistakes = group.Sum(a => a.Mistakes),
                LastAt = (DateTime?)group.Max(a => a.At),
            })
            .ToListAsync(ct);

        var byWord = tallies.ToLookup(t => t.WordId);

        ModeRecord Record(int wordId, string mode)
        {
            var row = byWord[wordId].FirstOrDefault(t => t.Mode == mode);
            return row is null
                ? new ModeRecord(0, 0, 0, null)
                : new ModeRecord(row.Attempts, row.Clean, row.Mistakes, row.LastAt);
        }

        return
        [
            .. words.Select(w => new WritingWord(
                w.Id, w.Characters, w.Pinyin, w.English,
                Record(w.Id, WritingMode.Copying),
                Record(w.Id, WritingMode.Memory)))
        ];
    }

    /// <summary>
    /// Records one character. The grading happened in the browser, where the strokes were - the
    /// same comparison the stroke-order box already does, run against what was drawn.
    /// </summary>
    public async Task<bool> RecordAsync(WritingAttempt attempt, CancellationToken ct)
    {
        if (!WritingMode.IsValid(attempt.Mode)) return false;
        if (attempt.Character.Length != 1) return false;
        if (!await db.VocabWords.AnyAsync(w => w.Id == attempt.WordId, ct)) return false;

        attempt.At = DateTime.UtcNow;
        db.WritingAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// The per-word summary the vocabulary page shows, for every word at once. One line on a row
    /// that is already there, rather than a reason to open anything.
    /// </summary>
    public async Task<Dictionary<int, (int Attempts, int Clean)>> SummaryAsync(CancellationToken ct)
    {
        var rows = await db.WritingAttempts
            .AsNoTracking()
            .GroupBy(a => a.WordId)
            .Select(group => new
            {
                WordId = group.Key,
                Attempts = group.Count(),
                Clean = group.Count(a => a.Mistakes == 0),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.WordId, r => (r.Attempts, r.Clean));
    }
}
