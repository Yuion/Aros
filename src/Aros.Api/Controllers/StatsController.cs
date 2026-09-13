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
    Aros.Api.Grammar.GrammarService grammar,
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
            mastered = rows.Count(r => Rung(r.Clip, r.Stat) is var (streak, ladder) && ladder.IsMastered(streak)),
            resting = rows.Count(r => Rung(r.Clip, r.Stat) is var (streak, ladder)
                                      && !ladder.IsMastered(streak)
                                      && ladder.IsResting(streak, r.Stat.LastSeenAt)),
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

        // Worst first — this is the study list, so a sentence you have retired is off it
        var needsWork = rows
            .Where(r => r.Stat.WrongCount > 0 && r.Clip.RetiredAt is null)
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

        var mastery = MasteryBands(rows.Select(r => Rung(r.Clip, r.Stat)));

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
            .Where(c => c.Stats.Count == 0 && c.RetiredAt is null)
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
            mastered = rows.Count(r => Rung(r.Word, r.Progress) is var (streak, ladder) && ladder.IsMastered(streak)),
            resting = rows.Count(r => Rung(r.Word, r.Progress) is var (streak, ladder)
                                      && !ladder.IsMastered(streak)
                                      && ladder.IsResting(streak, r.Progress.LastSeenAt)),
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

        // The study list, so a word you have retired is off it
        var needsWork = rows
            .Where(r => r.Progress.WrongCount > 0 && r.Word.RetiredAt is null)
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
            .Where(w => w.Progress.Count == 0 && !w.NeedsReview && w.RetiredAt is null)
            .OrderBy(w => w.Characters)
            .Select(w => w.Characters)
            .Take(30)
            .ToList();

        var historyStart = await db.VocabAnswers
            .OrderBy(a => a.AnsweredAt)
            .Select(a => (DateTime?)a.AnsweredAt)
            .FirstOrDefaultAsync(ct);

        var mastery = MasteryBands(rows.Select(r => Rung(r.Word, r.Progress)));
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
    /// <summary>
    /// The grammar trainer's side of the course. Patterns are scheduled rather than sentences, so
    /// the counts here are patterns — and the thing worth seeing is not the total but the shape of
    /// the tail: which patterns were taught once and never produced since.
    /// </summary>
    [HttpGet("grammar")]
    public async Task<IActionResult> Grammar(CancellationToken ct)
    {
        var overview = await grammar.OverviewAsync(ct);
        var standing = await grammar.AvailabilityAsync(ct);
        var lessons = await db.Lessons.AsNoTracking()
            .ToDictionaryAsync(l => l.Number, l => l.Date.ToString("yyyy-MM-dd"), ct);

        var practised = overview.Where(row => row.Progress is { } p && p.CorrectCount + p.WrongCount > 0).ToList();

        var correct = practised.Sum(row => row.Progress!.CorrectCount);
        var wrong = practised.Sum(row => row.Progress!.WrongCount);
        var answers = correct + wrong;

        var totals = new
        {
            patterns = overview.Count,
            withDrills = overview.Count(row => row.Items > 0),
            drills = overview.Sum(row => row.Items),
            practised = practised.Count,
            neverPractised = overview.Count(row => row.Items > 0 && row.Progress is null),
            noDrills = overview.Count(row => row.Items == 0),
            answers,
            correct,
            wrong,
            accuracy = answers == 0 ? (double?)null : (double)correct / answers,
            ready = standing.Ready,
            resting = standing.Resting,
            mastered = standing.Mastered,
            nextDueAt = standing.NextDueAt,
            nextDue = standing.NextDueAt is { } due ? Availability.Due(due) : null,
            lastPractised = practised.Count == 0 ? null : practised.Max(row => row.Progress!.LastSeenAt),
        };

        var since = DateTime.UtcNow.Date.AddDays(-(TrendDays - 1));

        var history = await db.GrammarAnswers
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

        // Worst first — the study list, and the reason the trainer exists
        var needsWork = practised
            .Where(row => row.Progress!.WrongCount > 0 && row.State != "mastered")
            .Select(row => new
            {
                title = row.Point.Title,
                attempts = row.Progress!.CorrectCount + row.Progress.WrongCount,
                correct = row.Progress.CorrectCount,
                wrong = row.Progress.WrongCount,
                accuracy = (double)row.Progress.CorrectCount / (row.Progress.CorrectCount + row.Progress.WrongCount),
                state = row.State,
            })
            .OrderBy(row => row.accuracy)
            .ThenByDescending(row => row.wrong)
            .Take(10)
            .ToList();

        // Taught, drillable, and never once produced cold
        var untouched = overview
            .Where(row => row.Items > 0 && row.Progress is null)
            .OrderBy(row => row.Point.IntroducedInLesson ?? int.MaxValue)
            .Select(row => new
            {
                title = row.Point.Title,
                introducedInLesson = row.Point.IntroducedInLesson,
                date = row.Point.IntroducedInLesson is { } n && lessons.TryGetValue(n, out var on) ? on : null,
                drills = row.Items,
            })
            .Take(20)
            .ToList();

        // A pattern with no drills cannot be practised at all, which is a gap in the library
        // rather than in the learning
        var missingDrills = overview
            .Where(row => row.Items == 0)
            .Select(row => new { title = row.Point.Title, introducedInLesson = row.Point.IntroducedInLesson })
            .ToList();

        // Patterns climb the vocabulary ladder, so the bands read the same way as the other tabs
        var mastery = MasteryBands(practised.Select(row =>
            (row.Progress!.ConsecutiveCorrect, RestSchedule.ForVocabulary(row.Progress.WrongCount))));

        var historyStart = await db.GrammarAnswers
            .OrderBy(a => a.AnsweredAt)
            .Select(a => (DateTime?)a.AnsweredAt)
            .FirstOrDefaultAsync(ct);

        return Ok(new { totals, daily, needsWork, untouched, missingDrills, mastery, historyStart, trendDays = TrendDays });
    }

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
            plan = lesson.Plan,
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

    /// <summary>Same for a word: retired by hand reads as finished, whatever the streak says.</summary>
    private static (int Streak, RestSchedule Schedule) Rung(VocabWord word, VocabProgress progress)
    {
        var ladder = VocabService.Ladder(progress);

        return word.RetiredAt is null
            ? (progress.ConsecutiveCorrect, ladder)
            : (ladder.MasteryStreak, ladder);
    }

    /// <summary>
    /// Where one sentence stands in one mode. A sentence retired by hand reads as finished however
    /// its streak actually stands: marking it mastered is a decision, not a claim about the
    /// streak, and the page should say the same thing the trainer does.
    /// </summary>
    private static (int Streak, RestSchedule Schedule) Rung(TtsClip clip, TtsClipStat stat)
    {
        var ladder = Ladder(stat);

        return clip.RetiredAt is null
            ? (stat.ConsecutiveCorrect, ladder)
            : (ladder.MasteryStreak, ladder);
    }

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
