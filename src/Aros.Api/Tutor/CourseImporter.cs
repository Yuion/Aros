using System.Text.Json;
using System.Text.Json.Serialization;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Tts;
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
    [property: JsonPropertyName("vocabulary")] List<WordSection>? Vocabulary,
    [property: JsonPropertyName("sentences")] List<WordSection>? Sentences,
    [property: JsonPropertyName("lesson_history")] List<LessonSection>? LessonHistory,
    [property: JsonPropertyName("preferences")] JsonElement? Preferences);

/// <summary>A word or a sentence as the write-up gives it — the same three columns either way.</summary>
public record WordSection(
    [property: JsonPropertyName("chinese")] string? Chinese,
    [property: JsonPropertyName("pinyin")] string? Pinyin,
    [property: JsonPropertyName("english")] string? English);

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
    [property: JsonPropertyName("plan")] string? Plan,
    [property: JsonPropertyName("next_recommended_topic")] string? NextRecommendedTopic,
    [property: JsonPropertyName("new_vocabulary")] List<JsonElement>? NewVocabulary,
    [property: JsonPropertyName("new_grammar")] List<JsonElement>? NewGrammar,
    [property: JsonPropertyName("reinforced")] List<JsonElement>? Reinforced,
    [property: JsonPropertyName("mistakes")] List<JsonElement>? Mistakes);

public record CourseImportResult(
    int Grammar, int Rules, int WeakPoints, int Resolved, int Lessons, bool CourseUpdated,
    int Words, int Sentences, int Drills, IReadOnlyList<string> Notes);

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
public class CourseImporter(
    AppDbContext db,
    VocabImporter vocabulary,
    TtsService tts,
    Grammar.GrammarLibrary grammarLibrary,
    ILogger<CourseImporter> logger)
{
    public async Task<CourseImportResult> ImportAsync(string json, CancellationToken ct) =>
        await ImportAsync(json, null, ct);

    /// <param name="runtimeId">
    /// The lesson being recorded, when this import is a lesson write-up rather than a pasted file.
    /// It binds the write-up to the messages that produced it.
    /// </param>
    public async Task<CourseImportResult> ImportAsync(string json, string? runtimeId, CancellationToken ct)
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
        var lessons = await ImportLessonsAsync(file.LessonHistory, runtimeId, notes, ct);
        var course = await ImportCourseAsync(file, ct);

        await db.SaveChangesAsync(ct);

        // Saving a lesson does the whole of it: the words go to the trainer's review queue, the
        // sentences are synthesized, and the drills the grammar trainer can now build are built.
        // Three buttons pressed in the right order was three chances to forget one.
        var words = await ImportWordsAsync(file.Vocabulary, notes, ct);
        var sentences = await ImportSentencesAsync(file.Sentences, notes, ct);
        var drills = (await grammarLibrary.RebuildAsync(ct)).Added;

        return new CourseImportResult(
            grammar, rules, weak, resolved, lessons, course, words, sentences, drills, notes);
    }

    /// <summary>
    /// The lesson's new words, through the same importer a pasted table uses and with the same
    /// rule: anything a model produced waits in review. A plausible wrong tone is exactly what it
    /// gets wrong, and a word drilled wrong is learned wrong.
    /// </summary>
    private async Task<int> ImportWordsAsync(List<WordSection>? items, List<string> notes, CancellationToken ct)
    {
        var rows = Rows(items);
        if (rows.Count == 0) return 0;

        var table = string.Join("\n", rows.Select(r => $"| {r.Chinese} | {r.Pinyin} | {r.English} |"));
        var result = await vocabulary.ImportAsync(table, ct, needsReview: true);

        if (result.Conflicts.Count > 0)
            notes.Add($"{result.Conflicts.Count} word(s) are held under a different reading and were left alone.");

        return result.Added + result.Updated;
    }

    /// <summary>How long to wait before going back over the sentences that failed.</summary>
    private static readonly TimeSpan SecondPassDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The lesson's listening sentences. Each new one is a paid synthesis, and synthesis does fail
    /// from time to time.
    ///
    /// Three layers of not losing the lesson over it. The client already retries a transient
    /// failure three times; whatever is still failing is then set aside and tried once more after
    /// a pause, since an outage that outlives three attempts a second apart is often over within
    /// the minute. And whatever fails even then is reported by name and skipped — saving a lesson
    /// must never fail because one sentence would not synthesize. The rest of the lesson is
    /// already written down by this point, and a missing clip can be added from the TTS page.
    /// </summary>
    private async Task<int> ImportSentencesAsync(List<WordSection>? items, List<string> notes, CancellationToken ct)
    {
        var rows = Rows(items);
        if (rows.Count == 0) return 0;

        var (added, failed) = await SpeakAsync(rows, ct);

        if (failed.Count > 0 && !ct.IsCancellationRequested)
        {
            logger.LogWarning("{Count} sentence(s) failed; trying again in {Delay}s", failed.Count, SecondPassDelay.TotalSeconds);

            try
            {
                await Task.Delay(SecondPassDelay, ct);
            }
            catch (OperationCanceledException)
            {
                // Nothing left to wait for; the sentences are still kept below
            }

            if (!ct.IsCancellationRequested)
            {
                var (more, stillFailing) = await SpeakAsync(failed, ct);
                added += more;
                failed = stillFailing;
            }
        }

        // Kept rather than lost: the sentence, its reading and its meaning go into the library
        // without a voice, and the TTS page can speak them in one press later
        foreach (var row in failed)
            await tts.KeepSilentAsync(row.Chinese, row.Pinyin, row.English, CancellationToken.None);

        if (failed.Count > 0)
            notes.Add(
                $"{failed.Count} sentence(s) could not be spoken and are held without audio — "
                + "open Chinese TTS and press \"Speak the missing\". Everything else was saved.");

        return added;
    }

    /// <summary>Consecutive failures that mean the service is down rather than the sentence is odd.</summary>
    private const int OutageAfter = 2;

    private async Task<(int Added, List<(string Chinese, string Pinyin, string English)> Failed)> SpeakAsync(
        List<(string Chinese, string Pinyin, string English)> rows, CancellationToken ct)
    {
        var added = 0;
        var failed = new List<(string, string, string)>();
        var inARow = 0;

        foreach (var row in rows)
        {
            // Asked to stop is not a failure to report: the rest simply do not happen
            if (ct.IsCancellationRequested) { failed.Add(row); continue; }

            // Two in a row is an outage, not a difficult sentence. Each attempt can run to the
            // sixty-second timeout and is tried three times, so carrying on through ten sentences
            // costs half an hour and fails all ten anyway — while the browser gave up long ago.
            if (inARow >= OutageAfter)
            {
                failed.Add(row);
                continue;
            }

            try
            {
                var (_, cached) = await tts.GetOrCreateAsync(row.Chinese, row.Pinyin, row.English, ct);
                if (!cached) added++;
                inARow = 0;
            }
            catch (Exception ex)
            {
                // Deliberately everything: a timeout arrives as TaskCanceledException, a dropped
                // connection as IOException, and either one taking the whole save down with it
                // would lose a lesson that has already happened
                logger.LogWarning(ex, "Sentence {Sentence} could not be synthesized", row.Chinese);
                failed.Add(row);
                inARow++;
            }
        }

        if (inARow >= OutageAfter)
            logger.LogWarning("Stopped synthesizing after {Count} failures in a row", inARow);

        return (added, failed);
    }

    private static List<(string Chinese, string Pinyin, string English)> Rows(List<WordSection>? items) =>
    [
        .. (items ?? [])
            .Select(i => (Chinese: (i.Chinese ?? "").Trim(), Pinyin: (i.Pinyin ?? "").Trim(), English: (i.English ?? "").Trim()))
            .Where(i => i.Chinese.Length > 0)
    ];

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

    private async Task<int> ImportLessonsAsync(
        List<LessonSection>? items, string? runtimeId, List<string> notes, CancellationToken ct)
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
            lesson.Plan = item.Plan ?? lesson.Plan;
            if (runtimeId is { Length: > 0 }) lesson.RuntimeId = runtimeId;
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
