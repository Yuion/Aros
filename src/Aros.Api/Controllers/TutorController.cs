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

[ApiController]
[Route("api/[controller]")]
public class TutorController(
    AppDbContext db,
    TutorService tutor,
    CourseState courseState,
    CourseImporter courseImporter,
    LessonRecorder recorder,
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
            messages = messages.Select(Describe),
        });
    }

    /// <summary>The non-streamed send, and the fallback when streaming is unavailable.</summary>
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] TutorMessageRequest request, CancellationToken ct)
    {
        try
        {
            var turn = await tutor.SendAsync(request.Text, ct);
            return Ok(new { question = Describe(turn.Question), answer = Describe(turn.Answer) });
        }
        catch (AiException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// The streamed send. Server-sent events, written straight to the response — note that nginx
    /// buffers proxied responses by default, so `proxy_buffering off` is needed on this route or
    /// the whole "stream" lands in one piece at the end.
    /// </summary>
    [HttpPost("stream")]
    public async Task Stream([FromBody] TutorMessageRequest request, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";        // asks nginx not to buffer, if it listens

        ChatMessage question;

        try
        {
            question = await tutor.BeginAsync(request.Text, ct);
        }
        catch (AiException ex)
        {
            await WriteAsync("error", new { message = ex.Message }, ct);
            return;
        }

        await WriteAsync("question", Describe(question), ct);

        var settings = await tutor.SettingsAsync(ct);
        var instructions = await tutor.InstructionsAsync(ct);

        var text = new StringBuilder();
        var final = new AiChunk(null, null, null, true);
        var started = Stopwatch.StartNew();
        string? error = null;

        try
        {
            await foreach (var chunk in tutor.StreamAsync(instructions, question.Content, settings.ConversationRef, ct))
            {
                if (chunk.Delta is { Length: > 0 } delta)
                {
                    text.Append(delta);
                    await WriteAsync("delta", new { text = delta }, ct);
                }

                if (chunk.Done) final = chunk;
            }
        }
        catch (OperationCanceledException)
        {
            error = "Cancelled.";                            // the user pressed stop; keep what arrived
        }
        catch (Exception ex)
        {
            error = ex.Message;
            await WriteAsync("error", new { message = ex.Message }, ct);
        }

        // Recorded even when it ended badly: a half-finished answer is still an answer
        await tutor.CompleteAsync(question, text.ToString(), final, (int)started.ElapsedMilliseconds, error,
            CancellationToken.None);

        var spend = await budget.StateAsync(CancellationToken.None);

        await WriteAsync("done", new
        {
            inputTokens = final.Usage?.InputTokens ?? 0,
            outputTokens = final.Usage?.OutputTokens ?? 0,
            latencyMs = (int)started.ElapsedMilliseconds,
            budget = new { used = spend.TokensUsedToday, limit = spend.DailyTokenBudget, left = spend.TokensLeft },
            error,
        }, CancellationToken.None);
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

    /// <summary>The exact text the model is given about the learner. The first thing to go wrong.</summary>
    [HttpGet("context")]
    public async Task<IActionResult> Context(CancellationToken ct)
    {
        var instructions = await tutor.InstructionsAsync(ct);

        return Ok(new
        {
            instructions,
            characters = instructions.Length,
            roughTokens = instructions.Length / 3,          // Chinese runs denser than English
        });
    }

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
