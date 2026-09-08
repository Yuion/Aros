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
public class StatsController(
    AppDbContext db,
    ListeningService listening,
    VocabService vocab,
    Tutor.LessonRuntimeService runtimeService) : ControllerBase
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

    /// <summary>
    /// The course as a chronicle: every recorded lesson in order, with what it brought in.
    ///
    /// Grammar and pronunciation rules attach by the lesson number they name, which is reliable.
    /// Vocabulary attaches by IntroducedInLesson where it is set and otherwise by matching the
    /// characters the write-up listed, because words imported from a table do not carry a lesson.
    /// Exercises attach by date: they are keyed to a runtime lesson id, which is not the same thing
    /// as a lesson number, and inventing a link would be worse than a rough one.
    /// </summary>
    [HttpGet("tutor")]
    public async Task<IActionResult> TutorChronicle(CancellationToken ct)
    {
        var lessons = await db.Lessons.AsNoTracking().OrderByDescending(l => l.Number).ToListAsync(ct);
        var grammar = await db.GrammarPoints.AsNoTracking().ToListAsync(ct);
        var rules = await db.PronunciationRules.AsNoTracking().ToListAsync(ct);
        var words = await db.VocabWords.AsNoTracking().ToListAsync(ct);
        var weak = await db.WeakPoints.AsNoTracking().ToListAsync(ct);
        var exercises = await db.Exercises.AsNoTracking().ToListAsync(ct);

        var runtime = await runtimeService.CurrentAsync(ct);

        var byCharacters = words
            .GroupBy(w => w.Characters)
            .ToDictionary(g => g.Key, g => g.First());

        var chronicle = lessons.Select(lesson => new
        {
            number = lesson.Number,
            date = lesson.Date,
            durationMinutes = lesson.DurationMinutes,
            summary = lesson.Summary,
            nextRecommendedTopic = lesson.NextRecommendedTopic,
            mistakes = lesson.MistakeNotes,
            reinforced = lesson.Reinforced,

            // Whatever the pool knows about the words the write-up named. An entry may be plain
            // characters, as a lesson write-up gives them, or the flattened
            // "本 · ben3 · measure word for books" that an imported course file produced — so the
            // characters are taken from the head of it either way.
            vocabulary = lesson.NewVocabulary
                .Select(entry =>
                {
                    var characters = Characters(entry);

                    return byCharacters.TryGetValue(characters, out var word)
                        ? new { characters, pinyin = word.Pinyin, english = word.English, known = true }
                        : new { characters, pinyin = "", english = Rest(entry, characters), known = false };
                })
                .ToList(),

            grammar = grammar
                .Where(g => g.IntroducedInLesson == lesson.Number)
                .Select(g => new { g.Title, g.Summary, status = g.Status.ToString() })
                .ToList(),

            // Named by the write-up but attached to a different lesson, or to none at all. Saying
            // which of the two is honest: "no entry" and "recorded under lesson 9" are not the
            // same situation, and the first would be wrong for most of these.
            grammarMentioned = lesson.NewGrammar
                .Where(name => !grammar.Any(g =>
                    g.IntroducedInLesson == lesson.Number && Matches(g.Title, name)))
                .Select(name =>
                {
                    var elsewhere = grammar.FirstOrDefault(g => Matches(g.Title, name));

                    return new
                    {
                        name,
                        recordedIn = elsewhere?.IntroducedInLesson,
                        title = elsewhere?.Title,
                    };
                })
                .ToList(),

            rules = rules
                .Where(r => r.IntroducedInLesson == lesson.Number)
                .Select(r => new { r.Title, r.Summary })
                .ToList(),

            exercises = exercises
                .Where(e => DateOnly.FromDateTime(e.SentAt.ToLocalTime()) == lesson.Date)
                .OrderBy(e => e.Id)
                .Select(e => new
                {
                    e.Key,
                    e.Type,
                    items = Tutor.CharacterBank.Read(e.ItemsJson).Count,
                    answered = e.AnsweredAt is not null,
                })
                .ToList(),
        }).ToList();

        // Words the pool holds that no lesson claims — imported by hand, or from before the tutor
        var claimed = lessons
            .SelectMany(l => l.NewVocabulary.Select(Characters))
            .ToHashSet();

        var unattributed = words.Count(w => w.IntroducedInLesson is null && !claimed.Contains(w.Characters));

        var messagesSinceLastLesson = await db.ChatMessages.AsNoTracking().CountAsync(m => !m.Hidden, ct);

        return Ok(new
        {
            totals = new
            {
                lessons = lessons.Count,
                first = lessons.Count == 0 ? null : lessons.Min(l => l.Date).ToString("yyyy-MM-dd"),
                last = lessons.Count == 0 ? null : lessons.Max(l => l.Date).ToString("yyyy-MM-dd"),
                minutes = lessons.Sum(l => l.DurationMinutes ?? 0),
                vocabulary = words.Count,
                unattributed,
                grammar = grammar.Count,
                rules = rules.Count,
                exercises = exercises.Count,
                exercisesAnswered = exercises.Count(e => e.AnsweredAt is not null),
                weakOpen = weak.Count(w => !w.Resolved),
                weakResolved = weak.Count(w => w.Resolved),
            },

            // A lesson under way has not been written up yet, and that is worth saying plainly
            inProgress = runtime.MinutesRequested is not null || runtime.ExercisesSentThisLesson.Count > 0
                ? new
                {
                    lessonId = runtime.LessonId,
                    phase = runtime.Phase.ToString(),
                    minutesRequested = runtime.MinutesRequested,
                    minutesElapsed = runtime.StartedAt is { } at ? (int)(DateTime.UtcNow - at).TotalMinutes : (int?)null,
                    exercisesSent = runtime.ExercisesSentThisLesson.Count,
                    newVocabulary = runtime.NewVocabularyThisLesson,
                    newGrammar = runtime.NewGrammarThisLesson,
                    messages = messagesSinceLastLesson,
                }
                : null,

            weakPoints = weak
                .OrderBy(w => w.Resolved)
                .ThenByDescending(w => w.Severity)
                .Select(w => new
                {
                    w.Target,
                    w.Type,
                    w.Severity,
                    kind = w.Kind.ToString(),
                    w.FirstSeen,
                    w.Resolved,
                    w.ResolvedAt,
                })
                .ToList(),

            lessons = chronicle,
        });
    }

    /// <summary>
    /// The word itself, out of an entry that may carry its reading and meaning alongside it.
    /// </summary>
    private static string Characters(string entry) =>
        entry.Split('·', StringSplitOptions.TrimEntries).FirstOrDefault() ?? entry.Trim();

    private static string Rest(string entry, string characters) =>
        entry.Length > characters.Length
            ? entry[characters.Length..].Trim(' ', '·')
            : "";

    /// <summary>Loose either way round: a title may name the pattern, or the pattern the title.</summary>
    private static bool Matches(string title, string name) =>
        title.Contains(name, StringComparison.OrdinalIgnoreCase)
        || name.Contains(title, StringComparison.OrdinalIgnoreCase);

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
