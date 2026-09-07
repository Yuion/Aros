using System.Text.Json;
using System.Text.Json.Serialization;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Vocab;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

// The shape of ChineseCourseState_v1.json. Vocabulary and sentences are deliberately absent from
// what this writes directly — see the note on ImportAsync.
public record CourseFile(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("course")] CourseSection? Course,
    [property: JsonPropertyName("grammar")] List<GrammarSection>? Grammar,
    [property: JsonPropertyName("pronunciation_rules")] List<RuleSection>? PronunciationRules,
    [property: JsonPropertyName("weak_points")] List<WeakSection>? WeakPoints,
    [property: JsonPropertyName("resolved_weak_points")] List<JsonElement>? ResolvedWeakPoints,
    [property: JsonPropertyName("lesson_history")] List<LessonSection>? LessonHistory,
    [property: JsonPropertyName("preferences")] JsonElement? Preferences);

public record CourseSection(
    [property: JsonPropertyName("level")] string? Level,
    [property: JsonPropertyName("current_lesson_topic")] string? CurrentLessonTopic,
    [property: JsonPropertyName("next_recommended_topic")] string? NextRecommendedTopic);

public record GrammarSection(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("introduced_lesson")] int? IntroducedLesson,
    [property: JsonPropertyName("examples")] List<JsonElement>? Examples);

public record RuleSection(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("introduced_lesson")] int? IntroducedLesson);

public record WeakSection(
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("expected")] string? Expected,
    [property: JsonPropertyName("severity")] int? Severity,
    [property: JsonPropertyName("notes")] string? Notes);

public record LessonSection(
    [property: JsonPropertyName("lesson_number")] int Number,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("duration_minutes")] int? DurationMinutes,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("next_recommended_topic")] string? NextRecommendedTopic,
    [property: JsonPropertyName("new_vocabulary")] List<JsonElement>? NewVocabulary,
    [property: JsonPropertyName("new_grammar")] List<JsonElement>? NewGrammar,
    [property: JsonPropertyName("reinforced")] List<JsonElement>? Reinforced,
    [property: JsonPropertyName("mistakes")] List<JsonElement>? Mistakes);

public record CourseImportResult(
    int Grammar, int Rules, int WeakPoints, int Resolved, int Lessons, bool CourseUpdated,
    IReadOnlyList<string> Notes);

/// <summary>
/// One-time import of the course as it stood in the previous chat tutor.
///
/// **It does not import vocabulary or sentences.** Both already exist in Aros, reconciled by hand
/// against an authoritative list, carrying real practice history and audio that was paid for. A
/// bulk insert from a JSON file would either duplicate them or overwrite readings that are already
/// correct. Words go through the vocabulary paste box and sentences through the TTS one, both of
/// which match on what is held and report what they will not touch.
///
/// What it does import is everything with no home yet: grammar, pronunciation rules, weak points,
/// lesson history and where the course had got to.
/// </summary>
public class CourseImporter(AppDbContext db)
{
    public async Task<CourseImportResult> ImportAsync(string json, CancellationToken ct)
    {
        CourseFile? file;

        try
        {
            file = JsonSerializer.Deserialize<CourseFile>(json);
        }
        catch (JsonException ex)
        {
            throw new AiException($"That is not valid JSON: {ex.Message}");
        }

        if (file is null) throw new AiException("The file was empty.");

        var notes = new List<string>();

        if (file.SchemaVersion is not (0 or 1))
            notes.Add($"File says schema_version {file.SchemaVersion}; this build understands 1.");

        var grammar = await ImportGrammarAsync(file.Grammar, notes, ct);
        var rules = await ImportRulesAsync(file.PronunciationRules, ct);
        var weak = await ImportWeakPointsAsync(file.WeakPoints, ct);
        var resolved = await ResolveWeakPointsAsync(file.ResolvedWeakPoints, ct);
        var lessons = await ImportLessonsAsync(file.LessonHistory, notes, ct);
        var course = await ImportCourseAsync(file, ct);

        await db.SaveChangesAsync(ct);

        notes.Add("Vocabulary and sentences were not touched — paste those through their own boxes, " +
                  "which match on what is already held.");

        return new CourseImportResult(grammar, rules, weak, resolved, lessons, course, notes);
    }

    private async Task<int> ImportGrammarAsync(List<GrammarSection>? items, List<string> notes, CancellationToken ct)
    {
        if (items is null) return 0;

        var held = await db.GrammarPoints.ToDictionaryAsync(g => g.Key, ct);
        var written = 0;

        foreach (var item in items)
        {
            var key = Key(item.Id, item.Title);
            if (key.Length == 0)
            {
                notes.Add("A grammar entry had neither id nor title and was skipped.");
                continue;
            }

            if (!held.TryGetValue(key, out var point))
            {
                point = new GrammarPoint { Key = key };
                db.GrammarPoints.Add(point);
                held[key] = point;
            }

            point.Title = item.Title ?? point.Title;
            point.Summary = item.Summary ?? point.Summary;
            point.Status = ParseStatus(item.Status);
            point.IntroducedInLesson = item.IntroducedLesson ?? point.IntroducedInLesson;
            point.Examples = Lines(item.Examples) ?? point.Examples;
            written++;
        }

        return written;
    }

    private async Task<int> ImportRulesAsync(List<RuleSection>? items, CancellationToken ct)
    {
        if (items is null) return 0;

        var held = await db.PronunciationRules.ToDictionaryAsync(r => r.Key, ct);
        var written = 0;

        foreach (var item in items)
        {
            var key = Key(item.Id, item.Title);
            if (key.Length == 0) continue;

            if (!held.TryGetValue(key, out var rule))
            {
                rule = new PronunciationRule { Key = key };
                db.PronunciationRules.Add(rule);
                held[key] = rule;
            }

            rule.Title = item.Title ?? rule.Title;
            rule.Summary = item.Summary ?? rule.Summary;
            rule.IntroducedInLesson = item.IntroducedLesson ?? rule.IntroducedInLesson;
            written++;
        }

        return written;
    }

    private async Task<int> ImportWeakPointsAsync(List<WeakSection>? items, CancellationToken ct)
    {
        if (items is null) return 0;

        var held = await db.WeakPoints.Where(w => !w.Resolved).ToListAsync(ct);
        var written = 0;

        foreach (var item in items)
        {
            var target = (item.Target ?? "").Trim();
            if (target.Length == 0) continue;

            var type = (item.Type ?? "").Trim();
            if (held.Any(w => w.Target == target && w.Type == type)) continue;

            db.WeakPoints.Add(new WeakPoint
            {
                Target = target,
                Kind = ParseKind(item.Kind),
                Type = type,
                Expected = item.Expected ?? "",
                Severity = Math.Clamp(item.Severity ?? 1, 1, 3),
                Notes = item.Notes,
            });

            written++;
        }

        return written;
    }

    /// <summary>
    /// Closes out weaknesses that have stopped being weaknesses. Resolving rather than deleting:
    /// the row stays, so a weakness that comes back is visibly a relapse rather than a new problem.
    /// </summary>
    private async Task<int> ResolveWeakPointsAsync(List<JsonElement>? targets, CancellationToken ct)
    {
        if (targets is null) return 0;

        var wanted = (Lines(targets) ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0) return 0;

        var open = await db.WeakPoints.Where(w => !w.Resolved).ToListAsync(ct);
        var resolved = 0;

        foreach (var weak in open.Where(w => wanted.Contains(w.Target.Trim())))
        {
            weak.Resolved = true;
            weak.ResolvedAt = DateTime.UtcNow;
            resolved++;
        }

        return resolved;
    }

    private async Task<int> ImportLessonsAsync(List<LessonSection>? items, List<string> notes, CancellationToken ct)
    {
        if (items is null) return 0;

        var held = await db.Lessons.ToDictionaryAsync(l => l.Number, ct);
        var written = 0;

        foreach (var item in items)
        {
            if (!held.TryGetValue(item.Number, out var lesson))
            {
                lesson = new Lesson { Number = item.Number };
                db.Lessons.Add(lesson);
                held[item.Number] = lesson;
            }

            lesson.Date = ParseDate(item.Date, notes, item.Number);
            lesson.DurationMinutes = item.DurationMinutes;
            lesson.Summary = item.Summary ?? "";
            lesson.NextRecommendedTopic = item.NextRecommendedTopic ?? "";
            lesson.NewVocabulary = Lines(item.NewVocabulary) ?? [];
            lesson.NewGrammar = Lines(item.NewGrammar) ?? [];
            lesson.Reinforced = Lines(item.Reinforced) ?? [];
            lesson.MistakeNotes = string.Join("; ", Lines(item.Mistakes) ?? []);
            written++;
        }

        return written;
    }

    private async Task<bool> ImportCourseAsync(CourseFile file, CancellationToken ct)
    {
        if (file.Course is null && file.Preferences is null) return false;

        var settings = await db.TutorSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new TutorSettings { Id = 1, Instructions = TutorInstructions.Default };
            db.TutorSettings.Add(settings);
        }

        if (file.Course is { } course)
        {
            settings.Level = course.Level ?? settings.Level;
            settings.CurrentLessonTopic = course.CurrentLessonTopic ?? settings.CurrentLessonTopic;
            settings.NextRecommendedTopic = course.NextRecommendedTopic ?? settings.NextRecommendedTopic;
        }

        if (file.Preferences is { } preferences)
            settings.PreferencesJson = preferences.GetRawText();

        return true;
    }

    /// <summary>
    /// A list whose entries may be plain strings or small objects. An entry like
    /// { "chinese": "我吃鸡。", "spoken_pinyin": "wo3 chi1 ji1", "meaning": "I eat chicken." } carries
    /// more than a bare string does, so it is flattened rather than rejected: whoever wrote the
    /// file should not have to impoverish it to fit the reader.
    /// </summary>
    private static List<string>? Lines(List<JsonElement>? items)
    {
        if (items is null) return null;

        var lines = new List<string>();

        foreach (var item in items)
        {
            var line = Line(item);
            if (line.Length > 0) lines.Add(line);
        }

        return lines;
    }

    private static string Line(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),

        JsonValueKind.Object => string.Join(" · ", element.EnumerateObject()
            .Select(p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : Line(p.Value))
            .Where(v => !string.IsNullOrWhiteSpace(v))),

        JsonValueKind.Array => string.Join(" · ", element.EnumerateArray().Select(Line).Where(v => v.Length > 0)),

        _ => "",
    };

    /// <summary>An explicit id if there is one, otherwise a slug of the title — never blank.</summary>
    private static string Key(string? id, string? title)
    {
        if (id is { Length: > 0 }) return id.Trim();
        if (title is not { Length: > 0 }) return "";

        var slug = new string(title.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray());

        return string.Join('_', slug.Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    private static DateOnly ParseDate(string? value, List<string> notes, int lessonNumber)
    {
        if (DateOnly.TryParse(value, out var date)) return date;

        if (value is { Length: > 0 }) notes.Add($"Lesson {lessonNumber}: could not read the date \"{value}\".");
        return DateOnly.FromDateTime(DateTime.Now);
    }

    private static GrammarStatus ParseStatus(string? value) => value?.ToLowerInvariant() switch
    {
        "learned" => GrammarStatus.Learned,
        "shaky" => GrammarStatus.Shaky,
        _ => GrammarStatus.Introduced,
    };

    /// <summary>
    /// Matched loosely on purpose. The value is written by hand or by another model, so
    /// "pronunciation_rule", "pronunciation" and "Pronunciation" all mean the same thing and none
    /// of them should quietly become "other".
    /// </summary>
    private static WeakPointKind ParseKind(string? value)
    {
        var text = (value ?? "").ToLowerInvariant();

        if (text.Contains("pronunc") || text.Contains("tone")) return WeakPointKind.Pronunciation;
        if (text.Contains("grammar")) return WeakPointKind.Grammar;
        if (text.Contains("word") || text.Contains("vocab")) return WeakPointKind.Word;

        return WeakPointKind.Other;
    }
}
