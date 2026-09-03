using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController(AppDbContext db) : ControllerBase
{
    private const int TrendDays = 30;

    /// <summary>The streaks shown one bar each; everything above them is resting or mastered.</summary>
    private static readonly int[] ActiveStreaks = [.. Enumerable.Range(0, DrawWeight.RestStreak)];

    [HttpGet("listening")]
    public async Task<IActionResult> Listening(CancellationToken ct)
    {
        var clips = await db.TtsClips
            .Include(c => c.Stat)
            .AsNoTracking()
            .ToListAsync(ct);

        var played = clips.Where(c => c.Stat is not null).ToList();

        var correct = played.Sum(c => c.Stat!.CorrectCount);
        var wrong = played.Sum(c => c.Stat!.WrongCount);
        var answers = correct + wrong;

        // Running totals cover all time, including rounds played before answer history existed.
        var totals = new
        {
            answers,
            correct,
            wrong,
            accuracy = answers == 0 ? (double?)null : (double)correct / answers,
            librarySize = clips.Count,
            practiced = played.Count,
            neverPracticed = clips.Count - played.Count,
            mastered = played.Count(c => DrawWeight.IsMastered(c.Stat!.ConsecutiveCorrect)),
            resting = played.Count(c => DrawWeight.IsResting(c.Stat!.ConsecutiveCorrect, c.Stat.LastSeenAt)),
            lastPlayed = played.Count == 0 ? null : played.Max(c => c.Stat!.LastSeenAt),
        };

        var since = DateTime.UtcNow.Date.AddDays(-(TrendDays - 1));

        var history = await db.ListeningAnswers
            .Where(a => a.AnsweredAt >= since)
            .AsNoTracking()
            .Select(a => new { a.AnsweredAt, a.Correct })
            .ToListAsync(ct);

        var daily = history
            .GroupBy(a => DateOnly.FromDateTime(a.AnsweredAt.ToLocalTime()))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                date = g.Key.ToString("yyyy-MM-dd"),
                answers = g.Count(),
                correct = g.Count(a => a.Correct),
                accuracy = (double)g.Count(a => a.Correct) / g.Count(),
            })
            .ToList();

        // Worst first — this is the study list
        var needsWork = played
            .Select(c => new
            {
                sentence = c.Sentence,
                attempts = c.Stat!.CorrectCount + c.Stat.WrongCount,
                correct = c.Stat.CorrectCount,
                wrong = c.Stat.WrongCount,
                accuracy = (double)c.Stat.CorrectCount / (c.Stat.CorrectCount + c.Stat.WrongCount),
            })
            .Where(c => c.wrong > 0)
            .OrderBy(c => c.accuracy)
            .ThenByDescending(c => c.wrong)
            .Take(10)
            .ToList();

        var mastery = MasteryBands(played.Select(c => c.Stat!.ConsecutiveCorrect));

        var untouched = clips
            .Where(c => c.Stat is null)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Sentence)
            .Take(20)
            .ToList();

        // Answer history only starts when logging was added — totals above predate it
        var historyStart = await db.ListeningAnswers
            .OrderBy(a => a.AnsweredAt)
            .Select(a => (DateTime?)a.AnsweredAt)
            .FirstOrDefaultAsync(ct);

        return Ok(new { totals, daily, needsWork, mastery, untouched, historyStart, trendDays = TrendDays });
    }

    [HttpGet("vocab")]
    public async Task<IActionResult> Vocab(CancellationToken ct)
    {
        var words = await db.VocabWords
            .Include(w => w.Progress)
            .AsNoTracking()
            .ToListAsync(ct);

        var rows = words.SelectMany(w => w.Progress.Select(p => new { Word = w, Progress = p })).ToList();

        var correct = rows.Sum(r => r.Progress.CorrectCount);
        var wrong = rows.Sum(r => r.Progress.WrongCount);
        var answers = correct + wrong;

        var totals = new
        {
            answers,
            correct,
            wrong,
            accuracy = answers == 0 ? (double?)null : (double)correct / answers,
            wordsTotal = words.Count,
            practiced = words.Count(w => w.Progress.Count > 0),
            neverPracticed = words.Count(w => w.Progress.Count == 0 && !w.NeedsReview),
            needsReview = words.Count(w => w.NeedsReview),
            mastered = rows.Count(r => DrawWeight.IsMastered(r.Progress.ConsecutiveCorrect)),
            resting = rows.Count(r => DrawWeight.IsResting(r.Progress.ConsecutiveCorrect, r.Progress.LastSeenAt)),
            lastPlayed = rows.Count == 0 ? null : rows.Max(r => r.Progress.LastSeenAt),
        };

        var since = DateTime.UtcNow.Date.AddDays(-(TrendDays - 1));

        var history = await db.VocabAnswers
            .Where(a => a.AnsweredAt >= since)
            .AsNoTracking()
            .Select(a => new { a.AnsweredAt, a.Correct })
            .ToListAsync(ct);

        var daily = history
            .GroupBy(a => DateOnly.FromDateTime(a.AnsweredAt.ToLocalTime()))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                date = g.Key.ToString("yyyy-MM-dd"),
                answers = g.Count(),
                correct = g.Count(a => a.Correct),
                accuracy = (double)g.Count(a => a.Correct) / g.Count(),
            })
            .ToList();

        // The point of tracking per direction: recognition and production come apart, and the
        // gap between them is the thing worth seeing.
        var byDirection = Enum.GetValues<VocabDirection>()
            .Select(direction =>
            {
                var forDirection = rows.Where(r => r.Progress.Direction == direction).ToList();
                var right = forDirection.Sum(r => r.Progress.CorrectCount);
                var total = right + forDirection.Sum(r => r.Progress.WrongCount);

                return new
                {
                    direction = direction.ToString(),
                    answers = total,
                    correct = right,
                    accuracy = total == 0 ? (double?)null : (double)right / total,
                };
            })
            .ToList();

        var needsWork = rows
            .Where(r => r.Progress.WrongCount > 0)
            .Select(r => new
            {
                characters = r.Word.Characters,
                pinyin = r.Word.Pinyin,
                direction = r.Progress.Direction.ToString(),
                attempts = r.Progress.CorrectCount + r.Progress.WrongCount,
                correct = r.Progress.CorrectCount,
                wrong = r.Progress.WrongCount,
                accuracy = (double)r.Progress.CorrectCount / (r.Progress.CorrectCount + r.Progress.WrongCount),
            })
            .OrderBy(r => r.accuracy)
            .ThenByDescending(r => r.wrong)
            .Take(10)
            .ToList();

        var untouched = words
            .Where(w => w.Progress.Count == 0 && !w.NeedsReview)
            .OrderBy(w => w.Characters)
            .Select(w => w.Characters)
            .Take(30)
            .ToList();

        var historyStart = await db.VocabAnswers
            .OrderBy(a => a.AnsweredAt)
            .Select(a => (DateTime?)a.AnsweredAt)
            .FirstOrDefaultAsync(ct);

        var mastery = MasteryBands(rows.Select(r => r.Progress.ConsecutiveCorrect));

        return Ok(new { totals, daily, byDirection, needsWork, mastery, untouched, historyStart, trendDays = TrendDays });
    }

    /// <summary>
    /// How far along the rest schedule everything is: one bar per streak up to the first rest,
    /// then the resting band, then what is done with. Counting the resting band by streak rather
    /// than by clock means an item that has come back but not yet been re-tested still shows here,
    /// which is where it belongs — it has not lost the streak, it just has not used it yet.
    /// </summary>
    private static List<object> MasteryBands(IEnumerable<int> streaks)
    {
        var all = streaks.ToList();

        var bands = ActiveStreaks
            .Select(streak => (object)new
            {
                label = streak.ToString(),
                count = all.Count(s => s == streak),
            })
            .ToList();

        bands.Add(new
        {
            label = $"Resting ({DrawWeight.RestStreak}–{DrawWeight.MasteryStreak - 1})",
            count = all.Count(s => s >= DrawWeight.RestStreak && !DrawWeight.IsMastered(s)),
        });

        bands.Add(new
        {
            label = "Mastered",
            count = all.Count(DrawWeight.IsMastered),
        });

        return bands;
    }
}
