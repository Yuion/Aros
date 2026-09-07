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
public record StartLessonRequest(int? Minutes);

[ApiController]
[Route("api/[controller]")]
public class TutorController(
    AppDbContext db,
    TutorService tutor,
    CourseState courseState,
    CourseImporter courseImporter,
    LessonRecorder recorder,
    TurnRunner runner,
    LessonRuntimeService runtimeService,
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

        // The exercise still on screen, if the learner has not answered it yet
        var pending = runtime.ExerciseKey is { Length: > 0 } && runtime.AwaitingUserAnswer
            ? await db.Exercises.AsNoTracking().FirstOrDefaultAsync(e => e.Key == runtime.ExerciseKey, ct)
            : null;

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
            runtime = Describe(runtime),
            exercise = pending is null ? null : Describe(pending),
            messages = messages.Select(Describe),
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
                warning = turn.Warning,
            });
        }
        catch (AiException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Begin a lesson of a given length. Clears anything left pending from last time and starts
    /// the clock, so the tutor can pace itself and wind down rather than stopping mid-exercise.
    /// </summary>
    [HttpPost("lesson/start")]
    public async Task<IActionResult> StartLesson([FromBody] StartLessonRequest request, CancellationToken ct)
    {
        var minutes = request.Minutes is { } m && m > 0 ? Math.Clamp(m, 5, 240) : (int?)null;
        var runtime = await runtimeService.ResetAsync(ct, minutes);

        return Ok(Describe(runtime));
    }

    /// <summary>
    /// "That was the lesson." Asks the tutor to write it up, and stores what comes back as a
    /// proposal — nothing reaches the course until it is approved.
    /// </summary>
    [HttpPost("lesson/end")]
    public async Task<IActionResult> EndLesson(CancellationToken ct)
    {
        try
        {
            await budget.RequireHeadroomAsync(ct);
            return Ok(Describe(await recorder.RecordAsync(ct)));
        }
        catch (AiException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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
            var result = await courseImporter.ImportAsync(proposal.Payload, ct);

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
        items = CharacterBank.Read(exercise.ItemsJson).Select(i => i.Prompt),
        characterBank = exercise.CharacterBank,
        answered = exercise.AnsweredAt is not null,
    };

    private static object Describe(LessonRuntime runtime) => new
    {
        lessonId = runtime.LessonId,
        phase = runtime.Phase.ToString(),
        minutesRequested = runtime.MinutesRequested,
        minutesElapsed = runtime.StartedAt is { } at ? (int)(DateTime.UtcNow - at).TotalMinutes : (int?)null,
        exerciseKey = runtime.ExerciseKey,
        awaitingUserAnswer = runtime.AwaitingUserAnswer,
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
