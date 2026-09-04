using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Listening;
using Aros.Api.Scheduling;
using Aros.Api.Vocab;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController(AppDbContext db, ListeningService listening, VocabService vocab) : ControllerBase
{
    private const int TrendDays = 30;



    [HttpGet("listening")]
    public async Task<IActionResult> Listening(CancellationToken ct)
    {
        var clips = await db.TtsClips
            .Include(c => c.Stats)
            .AsNoTracking()
            .ToListAsync(ct);

        // One row per sentence and mode, the way the vocabulary tab counts word and direction
        var rows = clips.SelectMany(c => c.Stats.Select(s => new { Clip = c, Stat = s })).ToList();
        var played = clips.Where(c => c.Stats.Count > 0).ToList();

        var correct = rows.Sum(r => r.Stat.CorrectCount);
        var wrong = rows.Sum(r => r.Stat.WrongCount);
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
            mastered = rows.Count(r => Ladder(r.Stat).IsMastered(r.Stat.ConsecutiveCorrect)),
            resting = rows.Count(r => Ladder(r.Stat).IsResting(r.Stat.ConsecutiveCorrect, r.Stat.LastSeenAt)),
            withPinyin = clips.Count(c => c.Pinyin.Length > 0),
            withEnglish = clips.Count(c => c.English.Length > 0),
            lastPlayed = rows.Count == 0 ? null : rows.Max(r => r.Stat.LastSeenAt),
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
        var needsWork = rows
            .Where(r => r.Stat.WrongCount > 0)
            .Select(r => new
            {
                sentence = r.Clip.Sentence,
                mode = r.Stat.Mode.ToString(),
                attempts = r.Stat.CorrectCount + r.Stat.WrongCount,
                correct = r.Stat.CorrectCount,
                wrong = r.Stat.WrongCount,
                accuracy = (double)r.Stat.CorrectCount / (r.Stat.CorrectCount + r.Stat.WrongCount),
            })
            .OrderBy(c => c.accuracy)
            .ThenByDescending(c => c.wrong)
            .Take(10)
            .ToList();

        var mastery = MasteryBands(rows.Select(r => (r.Stat.ConsecutiveCorrect, Ladder(r.Stat))));

        // Hearing a sentence and writing out what you heard are different skills
        var byMode = Enum.GetValues<ListeningMode>()
            .Select(mode =>
            {
                var forMode = rows.Where(r => r.Stat.Mode == mode).ToList();
                var right = forMode.Sum(r => r.Stat.CorrectCount);
                var total = right + forMode.Sum(r => r.Stat.WrongCount);

                return new
                {
                    mode = mode.ToString(),
                    answers = total,
                    correct = right,
                    accuracy = total == 0 ? (double?)null : (double)right / total,
                    available = mode switch
                    {
                        ListeningMode.Pinyin => clips.Count(c => c.Pinyin.Length > 0),
                        ListeningMode.English => clips.Count(c => c.English.Length > 0),
                        _ => clips.Count,
                    },
                };
            })
            .ToList();

        var untouched = clips
            .Where(c => c.Stats.Count == 0)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.Sentence)
            .Take(20)
            .ToList();

        // Answer history only starts when logging was added — totals above predate it
        var historyStart = await db.ListeningAnswers
            .OrderBy(a => a.AnsweredAt)
            .Select(a => (DateTime?)a.AnsweredAt)
            .FirstOrDefaultAsync(ct);

        var standing = Standing(await listening.AvailabilityAsync(ct));

        return Ok(new { totals, daily, byMode, standing, needsWork, mastery, untouched, historyStart, trendDays = TrendDays });
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
            mastered = rows.Count(r => RestSchedule.Vocabulary.IsMastered(r.Progress.ConsecutiveCorrect)),
            resting = rows.Count(r => RestSchedule.Vocabulary.IsResting(r.Progress.ConsecutiveCorrect, r.Progress.LastSeenAt)),
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

        var mastery = MasteryBands(
            rows.Select(r => (r.Progress.ConsecutiveCorrect, RestSchedule.Vocabulary)));
        var standing = Standing(await vocab.AvailabilityAsync(null, ct));

        return Ok(new { totals, daily, byDirection, standing, needsWork, mastery, untouched, historyStart, trendDays = TrendDays });
    }

    /// <summary>
    /// Where each direction or mode stands right now: what a round could draw on, what is waiting
    /// out a rest, and what is finished with. Same numbers the start button uses, so the page and
    /// the button can never disagree.
    /// </summary>
    private static List<object> Standing(IEnumerable<Availability> areas) =>
    [
        .. areas.Select(a => (object)new
        {
            key = a.Key,
            open = a.Ready,
            resting = a.Resting,
            mastered = a.Mastered,
            total = a.Total,
            nextDue = a.NextDueAt is { } due ? Availability.Due(due) : null,
        })
    ];

    private static RestSchedule Ladder(TtsClipStat stat) => RestSchedule.ForListening(stat.WrongCount);

    /// <summary>
    /// How far along everything is, one bar per streak and a last bar for what is finished with.
    /// Bars are counted by streak rather than by clock, so an item whose rest has expired but which
    /// has not been re-tested still shows at the streak it holds — it has not lost it, it just has
    /// not used it yet.
    ///
    /// The listening ladder is two ladders (a sentence missed once needs one more correct answer
    /// than one never missed), so the number of bars follows whichever is longest in the data.
    /// </summary>
    private static List<object> MasteryBands(IEnumerable<(int Streak, RestSchedule Schedule)> items)
    {
        var all = items.ToList();
        if (all.Count == 0) return [];

        var lastBar = all.Max(i => i.Schedule.LastActiveStreak);

        var bands = Enumerable.Range(0, lastBar + 1)
            .Select(streak => (object)new
            {
                label = streak.ToString(),
                count = all.Count(i => i.Streak == streak && !i.Schedule.IsMastered(i.Streak)),
            })
            .ToList();

        bands.Add(new
        {
            label = "Mastered",
            count = all.Count(i => i.Schedule.IsMastered(i.Streak)),
        });

        return bands;
    }
}
