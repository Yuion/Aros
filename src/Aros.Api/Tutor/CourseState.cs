using System.Globalization;
using System.Text.Json;
using System.Text;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

/// <summary>
/// Builds the block of text the tutor is given about the learner, read fresh from the database on
/// every send. It is assembled, never stored: the trainers own vocabulary, sentences and progress,
/// and a second copy would disagree with them within a week.
///
/// The listing is budgeted. A pool of forty words is nothing; a pool of five hundred is ten
/// thousand characters on every single message, so past a threshold the full list gives way to
/// counts plus the parts that actually steer a lesson — the weak, the recent, the untouched.
/// </summary>
public class CourseState(AppDbContext db)
{
    /// <summary>Above this many words, the full vocabulary list stops being sent.</summary>
    public const int FullListLimit = 250;

    public async Task<string> BuildAsync(CancellationToken ct)
    {
        var settings = await db.TutorSettings.AsNoTracking().FirstOrDefaultAsync(ct);

        var words = await db.VocabWords
            .Include(w => w.Progress)
            .Where(w => !w.NeedsReview && w.Active)
            .AsNoTracking()
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(ct);

        var sentences = await db.TtsClips.AsNoTracking().CountAsync(ct);
        var grammar = await db.GrammarPoints.AsNoTracking().OrderBy(g => g.IntroducedInLesson).ToListAsync(ct);
        var rules = await db.PronunciationRules.AsNoTracking().OrderBy(r => r.IntroducedInLesson).ToListAsync(ct);
        var weak = await db.WeakPoints.AsNoTracking().Where(w => !w.Resolved).ToListAsync(ct);
        var lessons = await db.Lessons.AsNoTracking().OrderByDescending(l => l.Number).Take(3).ToListAsync(ct);

        var review = await db.VocabWords.AsNoTracking().CountAsync(w => w.NeedsReview, ct);

        var text = new StringBuilder();
        text.AppendLine($"CURRENT LEARNING STATE (schema v{settings?.SchemaVersion ?? 1}, {DateTime.Now:yyyy-MM-dd})");
        text.AppendLine();
        text.AppendLine($"Level: {settings?.Level ?? "beginner"}");

        AppendVocabulary(text, words);
        AppendDirections(text, words);

        text.AppendLine();
        text.AppendLine($"Listening library: {sentences} sentences, already carrying pinyin and English.");
        if (review > 0)
            text.AppendLine($"{review} words are waiting in the review queue and are NOT yet being tested.");

        AppendList(text, "Grammar taught", grammar.Select(g => $"{g.Title} ({g.Status.ToString().ToLowerInvariant()})"));
        AppendList(text, "Pronunciation rules taught", rules.Select(r => r.Title));
        AppendList(text, "Open weak points", weak.Select(Describe));

        if (lessons.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Recent lessons:");
            foreach (var lesson in lessons.OrderBy(l => l.Number))
            {
                text.AppendLine($"  {lesson.Number} ({lesson.Date:yyyy-MM-dd}): {lesson.Summary}");
                if (lesson.NextRecommendedTopic.Length > 0)
                    text.AppendLine($"     next suggested: {lesson.NextRecommendedTopic}");
            }
        }

        AppendPreferences(text, settings?.PreferencesJson);

        text.AppendLine();
        if (settings?.CurrentLessonTopic is { Length: > 0 } current)
            text.AppendLine($"Current lesson topic: {current}");
        if (settings?.NextRecommendedTopic is { Length: > 0 } next)
            text.AppendLine($"Next recommended topic: {next}");

        return text.ToString().TrimEnd();
    }

    private static void AppendVocabulary(StringBuilder text, List<VocabWord> words)
    {
        text.AppendLine();

        if (words.Count == 0)
        {
            text.AppendLine("Vocabulary: none yet.");
            return;
        }

        if (words.Count <= FullListLimit)
        {
            text.AppendLine($"Vocabulary the learner knows ({words.Count}) — use freely:");
            text.AppendLine("  " + string.Join(" · ", words.Select(w => $"{w.Characters} {w.Pinyin} {w.English}")));
            return;
        }

        // Too many to list. Send what changes a lesson: the shaky, the newest, the neglected.
        var weakest = words
            .Where(w => w.Progress.Sum(p => p.WrongCount) > 0)
            .OrderByDescending(w => w.Progress.Sum(p => p.WrongCount))
            .Take(40);

        var newest = words.TakeLast(40);

        text.AppendLine($"Vocabulary the learner knows: {words.Count} words — too many to list in full.");
        text.AppendLine("  Most often missed: " + string.Join(" · ", weakest.Select(w => $"{w.Characters} {w.Pinyin}")));
        text.AppendLine("  Most recently added: " + string.Join(" · ", newest.Select(w => $"{w.Characters} {w.Pinyin}")));
        text.AppendLine("  Assume anything taught in an earlier lesson is known; ask if you need the full list.");
    }

    /// <summary>
    /// Which of the six directions is weakest, which is the one thing the trainers know that the
    /// tutor cannot see. Recognition and production come apart, and a lesson should lean on the
    /// side that is behind.
    /// </summary>
    private static void AppendDirections(StringBuilder text, List<VocabWord> words)
    {
        var rows = words.SelectMany(w => w.Progress).ToList();
        if (rows.Count == 0) return;

        var byDirection = Enum.GetValues<VocabDirection>()
            .Select(direction =>
            {
                var forDirection = rows.Where(p => p.Direction == direction).ToList();
                var answered = forDirection.Sum(p => p.CorrectCount + p.WrongCount);

                return (Direction: direction, Answered: answered,
                    Accuracy: answered == 0 ? (double?)null : (double)forDirection.Sum(p => p.CorrectCount) / answered);
            })
            .Where(d => d.Answered > 0)
            .OrderBy(d => d.Accuracy)
            .ToList();

        if (byDirection.Count == 0) return;

        text.AppendLine();
        text.AppendLine("Accuracy by direction in the trainer (weakest first):");
        foreach (var (direction, answered, accuracy) in byDirection)
            // Written out rather than :P0 — every culture's percent pattern has its own spacing,
            // and the server runs under whatever the machine is set to.
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Label(direction)}: {accuracy * 100:0}% of {answered}"));
    }

    private static string Label(VocabDirection direction) => direction switch
    {
        VocabDirection.CharactersToPinyin => "characters → pinyin",
        VocabDirection.CharactersToEnglish => "characters → English",
        VocabDirection.PinyinToEnglish => "pinyin → English",
        VocabDirection.EnglishToPinyin => "English → pinyin",
        VocabDirection.PinyinToCharacters => "pinyin → characters",
        _ => "English → characters",
    };

    private static string Describe(WeakPoint weak) =>
        $"{weak.Target} ({weak.Type}" +
        (weak.Expected.Length > 0 ? $", expected {weak.Expected}" : "") +
        $", severity {weak.Severity}, since {weak.FirstSeen:yyyy-MM-dd})";

    /// <summary>
    /// The imported preferences, flattened into readable lines rather than sent as raw JSON —
    /// cheaper in tokens and easier for a model to follow. They are the learner's own words about
    /// how they want to be taught, so they go in every message; a preference nobody sends is just
    /// a note to oneself.
    /// </summary>
    private static void AppendPreferences(StringBuilder text, string? json)
    {
        if (json is not { Length: > 2 }) return;

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return;                              // stored as written; unreadable means simply unsent
        }

        if (root.ValueKind != JsonValueKind.Object) return;

        var lines = new List<string>();
        Flatten(root, "", lines);
        if (lines.Count == 0) return;

        text.AppendLine();
        text.AppendLine("Teaching preferences (the learner's own, follow them):");
        foreach (var line in lines) text.AppendLine("  " + line);
    }

    private static void Flatten(JsonElement element, string prefix, List<string> lines)
    {
        foreach (var property in element.EnumerateObject())
        {
            // A leading underscore marks a note to whoever edits the file. Sending it would turn
            // an aside for the reader into an instruction for the model.
            if (property.Name.StartsWith('_')) continue;

            var name = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, name, lines);
                    break;

                case JsonValueKind.Array:
                    var items = property.Value.EnumerateArray()
                        .Select(i => i.ValueKind == JsonValueKind.String ? i.GetString() : i.ToString())
                        .Where(i => !string.IsNullOrWhiteSpace(i));
                    lines.Add($"{name}: {string.Join(", ", items)}");
                    break;

                case JsonValueKind.Null or JsonValueKind.Undefined:
                    break;

                default:
                    lines.Add($"{name}: {property.Value.ToString()}");
                    break;
            }
        }
    }

    private static void AppendList(StringBuilder text, string heading, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        text.AppendLine();
        text.AppendLine($"{heading} ({list.Count}):");
        text.AppendLine("  " + string.Join(" · ", list));
    }
}
