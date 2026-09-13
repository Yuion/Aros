using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Tutor;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Grammar;

public record LibraryReport(int Points, int Added, int Skipped, int FromExercises, int FromExamples);

/// <summary>
/// Builds the grammar trainer's question pool out of what the lessons already recorded. Nothing is
/// written for the trainer: the items are the exercises the tutor set during a lesson and the
/// examples it filed with each point. Both were worked through once with the tutor watching, which
/// is what makes them fair to ask again cold.
///
/// An exercise knows its lesson and a lesson's write-up lists the grammar it introduced, so items
/// inherit that lesson's points. An item can exercise more than one point and is filed under each:
/// a sentence is rarely about one pattern, and a drill that touches 不 while testing 去 is still a
/// fair test of both.
/// </summary>
public class GrammarLibrary(AppDbContext db, ILogger<GrammarLibrary> logger)
{
    /// <summary>Examples arrive flattened by the course importer: 我吃鸡。 · wo3 chi1 ji1 · I eat chicken.</summary>
    private const string ExampleSeparator = " · ";

    public async Task<LibraryReport> RebuildAsync(CancellationToken ct)
    {
        var points = await db.GrammarPoints.ToListAsync(ct);
        var held = await db.GrammarItems.AsNoTracking()
            .Select(i => new { i.GrammarPointId, i.Answer })
            .ToListAsync(ct);

        var seen = held.Select(h => (h.GrammarPointId, h.Answer)).ToHashSet();

        var added = 0;
        var skipped = 0;
        var fromExercises = 0;
        var fromExamples = 0;

        foreach (var (point, prompt, answer, source) in await CandidatesAsync(points, ct))
        {
            if (prompt.Length == 0 || answer.Length == 0) { skipped++; continue; }
            if (!seen.Add((point.Id, answer))) { skipped++; continue; }

            db.GrammarItems.Add(new GrammarItem
            {
                GrammarPointId = point.Id,
                Prompt = prompt,
                Answer = answer,
                Source = source,
            });

            added++;
            if (source == "example") fromExamples++; else fromExercises++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Grammar library rebuilt: {Added} added, {Skipped} skipped", added, skipped);

        return new LibraryReport(points.Count, added, skipped, fromExercises, fromExamples);
    }

    private async Task<List<(GrammarPoint Point, string Prompt, string Answer, string Source)>> CandidatesAsync(
        List<GrammarPoint> points, CancellationToken ct)
    {
        var candidates = new List<(GrammarPoint, string, string, string)>();

        // The examples file themselves: each one already belongs to its point
        foreach (var point in points)
        {
            foreach (var example in point.Examples)
            {
                var parts = example.Split(ExampleSeparator);
                if (parts.Length < 3) continue;

                candidates.Add((point, parts[^1].Trim(), parts[0].Trim(), "example"));
            }
        }

        // Exercises inherit the grammar of the lesson they were set in
        var byLesson = await PointsByLessonAsync(points, ct);
        var exercises = await db.Exercises.AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct);

        foreach (var exercise in exercises)
        {
            if (!byLesson.TryGetValue(exercise.LessonId, out var lessonPoints)) continue;

            foreach (var item in CharacterBank.Read(exercise.ItemsJson))
            {
                // Only sentences that ask for Chinese: a pinyin or translation task tests the
                // reading, not the pattern, and the trainer answers in characters
                if (!HasHan(item.ExpectedAnswer)) continue;

                foreach (var point in lessonPoints.Where(p => Exercises(p, item.ExpectedAnswer)))
                    candidates.Add((point, item.Prompt, item.ExpectedAnswer, exercise.Key));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Which grammar points each lesson's exercises belong to. A lesson's runtime id is a date and
    /// a time; its write-up carries only a date, so same-day lessons are paired in order — the
    /// first set of exercises that day belongs to the first lesson written up that day.
    /// </summary>
    private async Task<Dictionary<string, List<GrammarPoint>>> PointsByLessonAsync(
        List<GrammarPoint> points, CancellationToken ct)
    {
        var lessons = await db.Lessons.AsNoTracking().OrderBy(l => l.Number).ToListAsync(ct);
        var runtimeIds = await db.Exercises.AsNoTracking()
            .Select(e => e.LessonId)
            .Distinct()
            .ToListAsync(ct);

        var map = new Dictionary<string, List<GrammarPoint>>();

        foreach (var day in runtimeIds.GroupBy(id => id[..10]))
        {
            var sameDay = lessons.Where(l => l.Date.ToString("yyyy-MM-dd") == day.Key).ToList();
            var ordered = day.OrderBy(id => id).ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var lesson = i < sameDay.Count ? sameDay[i] : sameDay.LastOrDefault();
                if (lesson is null) continue;

                map[ordered[i]] = Matching(points, lesson);
            }
        }

        return map;
    }

    /// <summary>
    /// The points a lesson worked on: the ones it introduced, and the ones it reinforced. Matched
    /// on the title the write-up used, which is the only link the two halves share.
    /// </summary>
    private static List<GrammarPoint> Matching(List<GrammarPoint> points, Lesson lesson)
    {
        var named = lesson.NewGrammar.Concat(lesson.Reinforced).ToList();

        return
        [
            .. points.Where(p =>
                p.IntroducedInLesson == lesson.Number
                || named.Any(name => Mentions(name, p.Title)))
        ];
    }

    /// <summary>
    /// A write-up says "也" or "Also / too with 也"; a point is titled "Also / too with 也". Either
    /// containing the other counts, since neither side is written to a format.
    /// </summary>
    private static bool Mentions(string named, string title)
    {
        var a = named.Trim();
        var b = title.Trim();

        if (a.Length == 0 || b.Length == 0) return false;

        return a.Contains(b, StringComparison.OrdinalIgnoreCase)
               || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a sentence actually exercises a pattern, rather than merely sharing a lesson with
    /// it. A lesson that introduced seven points would otherwise file every one of its sentences
    /// under all seven, and "想 versus 要" would collect drills containing neither character.
    ///
    /// The test is the pattern's own title: it names the characters that make it what it is —
    /// "Subject + 在 + place", "不 + 坐 + transport + 去 + destination". All of them must appear,
    /// except where the title offers a choice between them, where one is enough.
    ///
    /// A title naming no characters at all ("Object omission when context is clear") cannot be
    /// checked this way, and keeps the lesson's own attribution.
    /// </summary>
    private static bool Exercises(GrammarPoint point, string answer)
    {
        var required = Tokens(point.Title);
        if (required.Count == 0) return true;

        return Alternatives(point.Title)
            ? required.Any(answer.Contains)
            : required.All(answer.Contains);
    }

    private static bool Alternatives(string title) =>
        title.Contains('／') || title.Contains('/') ||
        title.Contains(" versus ", StringComparison.OrdinalIgnoreCase) ||
        title.Contains(" or ", StringComparison.OrdinalIgnoreCase);

    /// <summary>The runs of Chinese in a title: 坐车 is one token, not two.</summary>
    private static List<string> Tokens(string title)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var c in title)
        {
            if (c is >= (char)0x4E00 and <= (char)0x9FFF)
            {
                current.Append(c);
                continue;
            }

            if (current.Length > 0) tokens.Add(current.ToString());
            current.Clear();
        }

        if (current.Length > 0) tokens.Add(current.ToString());

        return tokens;
    }

    private static bool HasHan(string text) =>
        text.Any(c => c is >= (char)0x4E00 and <= (char)0x9FFF);
}
