using System.Globalization;
using System.Text;
using System.Text.Json;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

/// <summary>
/// What the model is told about the learner, read fresh from the database on every send. It is
/// assembled, never stored: the trainers own vocabulary, sentences and progress, and a second copy
/// would disagree with them within a week.
///
/// It is deliberately compact. An earlier version sent three full lesson summaries, every audio
/// implementation detail and every resolved preference on every message — around 3,500 tokens in
/// which the rules that matter were buried. What survives is what changes the next turn: what the
/// learner knows, where they are weakest, what is being worked on, and how they want to be taught.
/// The rest stays in the database, where it can be read when it is wanted.
/// </summary>
public class CourseState(AppDbContext db)
{
    /// <summary>Above this many words, the inventory gives way to counts and the parts that steer.</summary>
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

        var grammar = await db.GrammarPoints.AsNoTracking().OrderBy(g => g.IntroducedInLesson).ToListAsync(ct);
        var rules = await db.PronunciationRules.AsNoTracking().OrderBy(r => r.IntroducedInLesson).ToListAsync(ct);
        var weak = await db.WeakPoints.AsNoTracking().Where(w => !w.Resolved).ToListAsync(ct);
        var lastLesson = await db.Lessons.AsNoTracking().OrderByDescending(l => l.Number).FirstOrDefaultAsync(ct);
        var review = await db.VocabWords.AsNoTracking().CountAsync(w => w.NeedsReview, ct);

        var text = new StringBuilder();
        text.AppendLine("CURRENT LEARNING STATE");
        text.AppendLine();

        text.AppendLine("LEARNER");
        text.AppendLine($"  {settings?.Level ?? "beginner"}. {words.Count} known vocabulary items.");
        if (review > 0) text.AppendLine($"  {review} more await review and must not be used yet.");

        AppendAccuracy(text, words);
        AppendTargets(text, weak, rules);

        text.AppendLine();
        text.AppendLine("CURRENT LESSON");
        text.AppendLine($"  {Blank(settings?.CurrentLessonTopic, "not set")}");
        if (settings?.NextRecommendedTopic is { Length: > 0 } next)
            text.AppendLine($"  planned next: {next}");
        if (lastLesson is not null)
            text.AppendLine($"  last lesson ({lastLesson.Number}, {lastLesson.Date:yyyy-MM-dd}): {lastLesson.Summary}");

        AppendPreferences(text, settings?.PreferencesJson);
        AppendInventory(text, words, grammar, rules);

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// The one thing the trainers know that no conversation can: recognition and production come
    /// apart, and a lesson should lean on whichever is behind.
    /// </summary>
    private static void AppendAccuracy(StringBuilder text, List<VocabWord> words)
    {
        var rows = words.SelectMany(w => w.Progress).ToList();
        if (rows.Count == 0) return;

        var scored = Enum.GetValues<VocabDirection>()
            .Select(direction =>
            {
                var forDirection = rows.Where(p => p.Direction == direction).ToList();
                var answered = forDirection.Sum(p => p.CorrectCount + p.WrongCount);

                return (Direction: direction, Answered: answered,
                    Accuracy: answered == 0 ? 0d : (double)forDirection.Sum(p => p.CorrectCount) / answered);
            })
            .Where(d => d.Answered > 0)
            .OrderBy(d => d.Accuracy)
            .ToList();

        if (scored.Count == 0) return;

        text.AppendLine();
        text.AppendLine("WEAKEST IN THE TRAINER");
        foreach (var row in scored.Take(2)) text.AppendLine("  " + Line(row.Direction, row.Accuracy, row.Answered));

        text.AppendLine("STRONGEST");
        foreach (var row in scored.TakeLast(2).Reverse())
            text.AppendLine("  " + Line(row.Direction, row.Accuracy, row.Answered));
    }

    private static string Line(VocabDirection direction, double accuracy, int answered) =>
        string.Create(CultureInfo.InvariantCulture, $"{Label(direction)}: {accuracy * 100:0}% of {answered}");

    private static void AppendTargets(StringBuilder text, List<WeakPoint> weak, List<PronunciationRule> rules)
    {
        var newest = rules.OrderByDescending(r => r.IntroducedInLesson ?? 0).Take(2).Select(r => r.Title);
        var targets = weak.Select(w => w.Target).Concat(newest).Distinct().ToList();

        if (targets.Count == 0) return;

        text.AppendLine();
        text.AppendLine("CURRENT TARGETS");
        foreach (var target in targets) text.AppendLine($"  {target}");
    }

    /// <summary>
    /// Preferences as written. Anything the standing instructions already say is worth deleting
    /// from the file rather than sending twice — a rule repeated is not followed twice, it just
    /// crowds out the rest.
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
            return;
        }

        if (root.ValueKind != JsonValueKind.Object) return;

        var lines = new List<string>();
        Flatten(root, "", lines);
        if (lines.Count == 0) return;

        text.AppendLine();
        text.AppendLine("TEACHING PREFERENCES");
        foreach (var line in lines) text.AppendLine("  " + line);
    }

    private static void AppendInventory(
        StringBuilder text, List<VocabWord> words, List<GrammarPoint> grammar, List<PronunciationRule> rules)
    {
        text.AppendLine();
        text.AppendLine("KNOWN VOCABULARY — use freely, and only these");

        if (words.Count == 0)
        {
            text.AppendLine("  none yet");
        }
        else if (words.Count <= FullListLimit)
        {
            text.AppendLine("  " + string.Join(" · ", words.Select(w => $"{w.Characters} {w.Pinyin} {w.English}")));
        }
        else
        {
            var missed = words.Where(w => w.Progress.Sum(p => p.WrongCount) > 0)
                .OrderByDescending(w => w.Progress.Sum(p => p.WrongCount)).Take(40);

            text.AppendLine($"  {words.Count} words, too many to list.");
            text.AppendLine("  most missed: " + string.Join(" · ", missed.Select(w => $"{w.Characters} {w.Pinyin}")));
            text.AppendLine("  newest: " + string.Join(" · ", words.TakeLast(40).Select(w => $"{w.Characters} {w.Pinyin}")));
        }

        if (grammar.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("KNOWN GRAMMAR");
            text.AppendLine("  " + string.Join(" · ", grammar.Select(g => g.Title)));
        }

        if (rules.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("PRONUNCIATION RULES TAUGHT");
            text.AppendLine("  " + string.Join(" · ", rules.Select(r => r.Title)));
        }
    }

    private static void Flatten(JsonElement element, string prefix, List<string> lines)
    {
        foreach (var property in element.EnumerateObject())
        {
            // A leading underscore marks a note to whoever edits the file, not an instruction
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
                    lines.Add($"{name}: {property.Value}");
                    break;
            }
        }
    }

    private static string Blank(string? value, string fallback) => value is { Length: > 0 } ? value : fallback;

    private static string Label(VocabDirection direction) => direction switch
    {
        VocabDirection.CharactersToPinyin => "characters -> pinyin",
        VocabDirection.CharactersToEnglish => "characters -> English",
        VocabDirection.PinyinToEnglish => "pinyin -> English",
        VocabDirection.EnglishToPinyin => "English -> pinyin",
        VocabDirection.PinyinToCharacters => "pinyin -> characters",
        _ => "English -> characters",
    };
}
