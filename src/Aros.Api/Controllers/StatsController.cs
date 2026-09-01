using Aros.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController(AppDbContext db) : ControllerBase
{
    private const int MasteredStreak = 3;
    private const int TrendDays = 30;

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
            mastered = played.Count(c => c.Stat!.ConsecutiveCorrect >= MasteredStreak),
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

        var mastery = new[] { 0, 1, 2, MasteredStreak }
            .Select(streak => new
            {
                streak,
                label = streak >= MasteredStreak ? $"{MasteredStreak}+" : streak.ToString(),
                count = streak >= MasteredStreak
                    ? played.Count(c => c.Stat!.ConsecutiveCorrect >= MasteredStreak)
                    : played.Count(c => c.Stat!.ConsecutiveCorrect == streak),
            })
            .ToList();

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
}
