using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Tutor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

public record TutorMessageRequest(string? Text);
public record CourseFileRequest(string? Json);
public record StartSessionRequest(string? Mode);

[ApiController]
[Route("api/[controller]")]
public class TutorController(
    AppDbContext db,
    TutorService tutor,
    CourseImporter courseImporter,
    LessonRecorder recorder,
    TurnRunner runner,
    LessonRuntimeService runtimeService,
    WeakPointReview weakPoints,
    Aros.Api.Syllabus.SyllabusService syllabus,
    Aros.Api.Tts.TtsService tts,
    AiBudget budget,
    Microsoft.Extensions.Options.IOptions<AiOptions> options) : ControllerBase
{
    private readonly AiOptions _options = options.Value;

    /// <summary>Everything the page needs to draw itself, in one call.</summary>
    [HttpGet]
    public async Task<IActionResult> State([FromQuery] int history = 100, CancellationToken ct = default)
    {
        var settings = await tutor.SettingsAsync(ct);
        var messages = await tutor.HistoryAsync(Math.Clamp(history, 1, 500), ct);
        var spend = await budget.StateAsync(ct);
        var runtime = await runtimeService.CurrentAsync(ct);
        var syllabusProgress = await syllabus.ProgressAsync(ct);

        // Every exercise the visible thread refers to, so each one renders where it was set
        var keys = messages.Where(m => m.ExerciseKey is not null).Select(m => m.ExerciseKey!).ToList();

        var exercises = await db.Exercises
            .AsNoTracking()
            .Where(e => keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, ct);

        // Same for the texts the thread refers to: each one renders where it was set
        var textIds = messages.Where(m => m.TextId is not null).Select(m => m.TextId!.Value).ToList();

        var texts = await db.TutorTexts
            .AsNoTracking()
            .Where(t => textIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        return Ok(new
        {
            configured = _options.IsConfigured,
            problem = _options.IsConfigured ? null : _options.ConfigurationProblem,
            model = _options.Model,
            conversation = settings.ConversationRef is not null,
            conversationStartedAt = settings.ConversationStartedAt,
            level = settings.Level,
            currentTopic = settings.CurrentLessonTopic,
            nextTopic = settings.NextRecommendedTopic,
            budget = new
            {
                used = spend.TokensUsedToday,
                limit = spend.DailyTokenBudget,
                left = spend.TokensLeft,
                requestsThisHour = spend.RequestsThisHour,
            },
            // The list the course is aiming at, so the page can say what "next lesson" is for
            syllabus = new
            {
                level = syllabusProgress.Level,
                total = syllabusProgress.Total,
                taught = syllabusProgress.Taught,
            },
            runtime = Describe(runtime),
            messages = messages.Select(m => Describe(m, exercises, texts)),
        });
    }

    /// <summary>
    /// One turn. The reply is structured rather than free text, so the exercise arrives as data:
    /// the application builds the character bank from the expected answers, gives the exercise an
    /// identity, and refuses one it has set before.
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] TutorMessageRequest request, CancellationToken ct)
    {
        try
        {
            var turn = await runner.RunAsync(request.Text, ct);

            return Ok(new
            {
                question = Describe(turn.Question),
                answer = Describe(turn.Answer),
                exercise = turn.Exercise is null ? null : Describe(turn.Exercise),
                text = turn.Text is null ? null : Describe(turn.Text),
                warning = turn.Warning,
            });
        }
        catch (AiException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Begin a session, in one of the three modes. Clears anything left pending from last time,
    /// so a session never starts halfway through an exercise nobody finished.
    ///
    /// No length is asked for any more. The tutor was told how many minutes were wanted and how
    /// far through it was, and it changed nothing: a lesson ran as long as it ran. A number that
    /// steers nothing is a question not worth asking.
    /// </summary>
    [HttpPost("lesson/start")]
    public async Task<IActionResult> StartLesson([FromBody] StartSessionRequest? request, CancellationToken ct)
    {
        var mode = ParseMode(request?.Mode);

        // Before the tutor is told what you are weak at, the trainers get to say which of those
        // weaknesses they have since disproved. Otherwise a problem fixed a fortnight ago still
        // shapes the lesson.
        if (mode == TutorMode.Lesson) await weakPoints.SweepAsync(ct);

        var runtime = await runtimeService.StartAsync(mode, ct);

        return Ok(Describe(runtime));
    }

    /// <summary>
    /// "That was the lesson." Asks the tutor to write it up, and stores what comes back as a
    /// proposal — nothing reaches the course until it is approved.
    /// </summary>
    [HttpPost("lesson/end")]
    public async Task<IActionResult> EndLesson(CancellationToken ct)
    {
        var runtime = await runtimeService.CurrentAsync(ct);

        // Only a lesson is written up. A conversation has nothing to record — the trainers hold
        // the practice and the course holds the material — and a reading text is already saved
        // as itself, so asking the model to summarise either is a bill for nothing.
        if (runtime.Mode != TutorMode.Lesson)
        {
            var what = ModeRules.Name(runtime.Mode);
            await runtimeService.CloseAsync(runtime, ct);

            return Ok(new { recorded = false, message = $"Ended the {what}. Only lessons are written up." });
        }

        // A lesson nothing was said in has nothing to write up, and asking the model to write one
        // anyway fails — which used to leave the lesson marked as running with no way to end it.
        var settings = await tutor.SettingsAsync(ct);

        if (settings.ConversationRef is not { Length: > 0 })
        {
            await runtimeService.CloseAsync(runtime, ct);

            return Ok(new { recorded = false, message = "Nothing was said in that lesson, so there was nothing to write up." });
        }

        try
        {
            await budget.RequireHeadroomAsync(ct);
            var proposal = await recorder.RecordAsync(ct);

            // The write-up is the end of the lesson: the page goes back to offering the next one
            await runtimeService.CloseAsync(runtime, ct);

            return Ok(Describe(proposal));
        }
        catch (AiException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Every text the tutor has written, newest first. They are kept whatever you thought of them
    /// at the time — deciding later that one was good is no use if it was never written down.
    /// </summary>
    [HttpGet("texts")]
    public async Task<IActionResult> Texts(CancellationToken ct)
    {
        var texts = await db.TutorTexts
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Ok(texts.Select(Describe));
    }

    [HttpGet("texts/{id:int}")]
    public async Task<IActionResult> Text(int id, CancellationToken ct)
    {
        var text = await db.TutorTexts.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

        return text is null ? NotFound() : Ok(Describe(text));
    }

    /// <summary>
    /// Speaks a text, once. The file is named after its own content and belongs to this text
    /// alone: it is not a listening sentence, gets no score, and is never drawn by a trainer.
    /// </summary>
    [HttpPost("texts/{id:int}/speak")]
    public async Task<IActionResult> SpeakText(int id, CancellationToken ct)
    {
        var text = await db.TutorTexts.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (text is null) return NotFound();

        try
        {
            text.AudioLocation = await tts.SpeakFragmentAsync(text.Chinese, ct);
            text.SpokenAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Ok(Describe(text));
        }
        catch (Tts.TtsException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("texts/{id:int}/audio")]
    public async Task<IActionResult> TextAudio(int id, CancellationToken ct)
    {
        var text = await db.TutorTexts.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

        if (text is null || text.AudioLocation.Length == 0 || !tts.FileExists(text.AudioLocation))
            return NotFound();

        return File(tts.OpenFile(text.AudioLocation), "audio/mpeg", enableRangeProcessing: true);
    }

    /// <summary>
    /// Forgets a text. The audio file is left alone: it is named after its content, so another
    /// text with the same words would want the very same file, and the sweeper is the one place
    /// that decides a file is unreferenced.
    /// </summary>
    [HttpDelete("texts/{id:int}")]
    public async Task<IActionResult> DeleteText(int id, CancellationToken ct)
    {
        var text = await db.TutorTexts.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (text is null) return NotFound();

        db.TutorTexts.Remove(text);
        await db.SaveChangesAsync(ct);

        return Ok(new { deleted = true });
    }

    [HttpGet("proposals")]
    public async Task<IActionResult> Proposals(CancellationToken ct)
    {
        var proposals = await db.TutorProposals
            .AsNoTracking()
            .Where(p => p.Status == ProposalStatus.Pending)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        return Ok(proposals.Select(Describe));
    }

    /// <summary>Applies a proposal through the same importer a pasted file goes through.</summary>
    [HttpPost("proposals/{id:int}/apply")]
    public async Task<IActionResult> ApplyProposal(int id, CancellationToken ct)
    {
        var proposal = await db.TutorProposals.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proposal is null) return NotFound();
        if (proposal.Status != ProposalStatus.Pending)
            return BadRequest(new { message = $"That proposal was already {proposal.Status.ToString().ToLowerInvariant()}." });

        try
        {
            // The runtime still knows which lesson this was; after the reset below, nothing does
            var runtime = await runtimeService.CurrentAsync(ct);
            var result = await courseImporter.ImportAsync(proposal.Payload, runtime.LessonId, ct);

            proposal.Status = ProposalStatus.Applied;
            proposal.DecidedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // The lesson is recorded, so nothing should still be pending from it
            await runtimeService.ResetAsync(ct);

            return Ok(new
            {
                grammar = result.Grammar,
                rules = result.Rules,
                weakPoints = result.WeakPoints,
                resolved = result.Resolved,
                lessons = result.Lessons,
                words = result.Words,
                sentences = result.Sentences,
                drills = result.Drills,
                notes = result.Notes,
            });
        }
        catch (AiException ex)
        {
            // The proposal stays pending: a payload that would not import is worth looking at,
            // not silently discarding.
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("proposals/{id:int}/reject")]
    public async Task<IActionResult> RejectProposal(int id, CancellationToken ct)
    {
        var proposal = await db.TutorProposals.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proposal is null) return NotFound();

        proposal.Status = ProposalStatus.Rejected;
        proposal.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Clears the chat and forgets OpenAI's thread. Everything learned stays.</summary>
    [HttpPost("conversation/new")]
    public async Task<IActionResult> NewConversation(CancellationToken ct) =>
        Ok(new { cleared = await tutor.NewConversationAsync(ct) });

    /// <summary>
    /// The exact text the model is given, in its two halves: the standing instructions, which are
    /// yours to edit, and the state, which is read from the database and cannot be typed over.
    /// The first thing to go wrong is usually in here.
    /// </summary>
    /// <summary>
    /// One lesson's transcript, exactly as it happened. Hidden messages are included: clearing the
    /// thread is about the screen, not about the record, and a lesson you cleared afterwards is
    /// still a lesson you had.
    /// </summary>
    [HttpGet("lessons/{number:int}/transcript")]
    public async Task<IActionResult> Transcript(int number, CancellationToken ct)
    {
        var lesson = await db.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Number == number, ct);
        if (lesson is null) return NotFound();

        var messages = lesson.RuntimeId.Length == 0
            ? []
            : await db.ChatMessages
                .AsNoTracking()
                .Where(m => m.LessonId == lesson.RuntimeId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);

        var keys = messages.Where(m => m.ExerciseKey is not null).Select(m => m.ExerciseKey!).ToList();
        var exercises = await db.Exercises.AsNoTracking()
            .Where(e => keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, ct);

        return Ok(new
        {
            number,
            date = lesson.Date,
            runtimeId = lesson.RuntimeId,
            messages = messages.Select(m => Describe(m, exercises)),
        });
    }

    [HttpGet("context")]
    public async Task<IActionResult> Context(CancellationToken ct)
    {
        var (standing, state, runtime) = await tutor.PartsAsync(ct);
        var whole = TutorService.Join(standing, state, runtime);

        return Ok(new
        {
            instructions = whole,
            standing,
            state,
            runtime,
            isDefault = standing.Trim() == TutorInstructions.Default.Trim(),
            characters = whole.Length,
            roughTokens = whole.Length / 3,                 // Chinese runs denser than English
        });
    }

    /// <summary>Rewrite the standing instructions. An empty body restores the built-in text.</summary>
    [HttpPut("instructions")]
    public async Task<IActionResult> SetInstructions(
        [FromBody] TutorMessageRequest request, CancellationToken ct)
    {
        var saved = await tutor.SetInstructionsAsync(request.Text, ct);

        return Ok(new
        {
            standing = saved,
            isDefault = saved.Trim() == TutorInstructions.Default.Trim(),
        });
    }

    /// <summary>The built-in text, for comparing against or reverting to.</summary>
    [HttpGet("instructions/default")]
    public IActionResult DefaultInstructions() => Ok(new { standing = TutorInstructions.Default });

    /// <summary>The exact schema the lesson write-up is held to. Useful when a write-up is refused.</summary>
    [HttpGet("lesson/schema")]
    public IActionResult LessonSchemaDefinition() => Ok(new
    {
        name = Tutor.LessonSchema.Definition.Name,
        schema = Tutor.LessonSchema.Definition.Definition,
    });

    [HttpGet("course")]
    public async Task<IActionResult> Course(CancellationToken ct) => Ok(new
    {
        grammar = await db.GrammarPoints.AsNoTracking().OrderBy(g => g.IntroducedInLesson).ThenBy(g => g.Id)
            .Select(g => new { g.Id, g.Key, g.Title, g.Summary, status = g.Status.ToString(), g.IntroducedInLesson })
            .ToListAsync(ct),

        rules = await db.PronunciationRules.AsNoTracking().OrderBy(r => r.IntroducedInLesson).ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.Key, r.Title, r.Summary, r.IntroducedInLesson })
            .ToListAsync(ct),

        weakPoints = await db.WeakPoints.AsNoTracking().Where(w => !w.Resolved).OrderBy(w => w.Target)
            .Select(w => new { w.Id, w.Target, kind = w.Kind.ToString(), w.Type, w.Expected, w.Severity, w.FirstSeen })
            .ToListAsync(ct),

        lessons = await db.Lessons.AsNoTracking().OrderByDescending(l => l.Number)
            .Select(l => new { l.Id, l.Number, l.Date, l.Summary, l.NextRecommendedTopic, l.NewVocabulary, l.NewGrammar })
            .ToListAsync(ct),
    });

    /// <summary>Imports ChineseCourseState_v1.json — grammar, rules, weak points, lessons, position.</summary>
    [HttpPost("course/import")]
    public async Task<IActionResult> ImportCourse([FromBody] CourseFileRequest request, CancellationToken ct)
    {
        try
        {
            var result = await courseImporter.ImportAsync(request.Json ?? "", ct);

            return Ok(new
            {
                grammar = result.Grammar,
                rules = result.Rules,
                weakPoints = result.WeakPoints,
                resolved = result.Resolved,
                lessons = result.Lessons,
                courseUpdated = result.CourseUpdated,
                notes = result.Notes,
            });
        }
        catch (AiException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Ask the trainers which open weaknesses they have disproved. Runs when a lesson starts as
    /// well; this is for when you want the list tidied without starting one.
    /// </summary>
    [HttpPost("weak-points/sweep")]
    public async Task<IActionResult> SweepWeakPoints(CancellationToken ct) =>
        Ok(new { resolved = await weakPoints.SweepAsync(ct) });

    [HttpDelete("weak-points/{id:int}")]
    public async Task<IActionResult> ResolveWeakPoint(int id, CancellationToken ct)
    {
        var weak = await db.WeakPoints.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (weak is null) return NotFound();

        weak.Resolved = true;
        weak.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>The exercise as the page needs it — never the expected answers.</summary>
    private static object Describe(Data.Entities.Exercise exercise) => new
    {
        key = exercise.Key,
        type = exercise.Type,
        instructions = exercise.Instructions,
        items = CharacterBank.Read(exercise.ItemsJson)
            .Select(i => new { prompt = i.Prompt, pinyin = i.PromptPinyin }),
        characterBank = exercise.CharacterBank,
        answered = exercise.AnsweredAt is not null,
    };

    /// <summary>
    /// A text as the page needs it. The translation travels with it rather than behind a second
    /// call — it is the answer key, and hiding it is the page's job, not the network's.
    /// </summary>
    private static object Describe(TutorText text) => new
    {
        text.Id,
        text.Title,
        text.Chinese,
        text.Pinyin,
        text.English,
        text.Notes,
        text.WordsUsed,
        text.GrammarUsed,
        text.NewWords,
        text.CreatedAt,
        text.SpokenAt,
        hasAudio = text.AudioLocation.Length > 0,
        audioUrl = text.AudioLocation.Length > 0 ? $"/api/tutor/texts/{text.Id}/audio" : null,
        characters = text.Chinese.Count(c => c >= 0x4E00 && c <= 0x9FFF),
    };

    /// <summary>The mode names the page sends. Anything unrecognised is a lesson.</summary>
    private static TutorMode ParseMode(string? mode) => (mode ?? "").Trim().ToLowerInvariant() switch
    {
        "talk" or "conversation" => TutorMode.Talk,
        "text" or "reading" => TutorMode.Text,
        _ => TutorMode.Lesson,
    };

    private static object Describe(LessonRuntime runtime) => new
    {
        lessonId = runtime.LessonId,
        mode = runtime.Mode.ToString().ToLowerInvariant(),
        phase = runtime.Phase.ToString(),
        running = runtime.StartedAt is not null,
        minutesElapsed = runtime.StartedAt is { } at ? (int)(DateTime.UtcNow - at).TotalMinutes : (int?)null,
        exerciseKey = runtime.ExerciseKey,
        awaitingUserAnswer = runtime.AwaitingUserAnswer,
        plan = runtime.Plan,
        exercisesSent = runtime.ExercisesSentThisLesson.Count,
        newVocabulary = runtime.NewVocabularyThisLesson,
        newGrammar = runtime.NewGrammarThisLesson,
    };

    private static object Describe(TutorProposal proposal) => new
    {
        id = proposal.Id,
        summary = proposal.Summary,
        payload = proposal.Payload,
        createdAt = proposal.CreatedAt,
        inputTokens = proposal.InputTokens,
        outputTokens = proposal.OutputTokens,
    };

    private async Task WriteAsync(string type, object payload, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(payload)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private static object Describe(
        ChatMessage message,
        IReadOnlyDictionary<string, Data.Entities.Exercise> exercises,
        IReadOnlyDictionary<int, TutorText>? texts = null)
    {
        var described = Describe(message);

        var carried = message.ExerciseKey is { } key && exercises.TryGetValue(key, out var exercise)
            ? Describe(exercise)
            : null;

        var text = message.TextId is { } id && texts is not null && texts.TryGetValue(id, out var found)
            ? Describe(found)
            : null;

        return new { message = described, exercise = carried, text };
    }

    private static object Describe(ChatMessage message) => new
    {
        id = message.Id,
        role = message.Role.ToString().ToLowerInvariant(),
        content = message.Content,
        createdAt = message.CreatedAt,
        model = message.Model,
        inputTokens = message.InputTokens,
        outputTokens = message.OutputTokens,
        latencyMs = message.LatencyMs,
        failed = message.Failed,
        error = message.ErrorMessage,
    };
}
