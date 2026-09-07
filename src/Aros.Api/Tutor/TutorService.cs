using System.Diagnostics;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

public record TutorTurn(ChatMessage Question, ChatMessage Answer);

/// <summary>
/// Sending a message and keeping the record of it. Two rules shape the whole class:
///
/// the typed message is saved *before* the request leaves, so a failure never eats it; and the
/// course state is rebuilt on every send, so the tutor is never working from a stale copy of what
/// the learner knows.
/// </summary>
public class TutorService(
    AppDbContext db,
    OpenAiClient client,
    CourseState courseState,
    AiBudget budget,
    ILogger<TutorService> logger)
{
    public async Task<TutorSettings> SettingsAsync(CancellationToken ct)
    {
        var settings = await db.TutorSettings.FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;

        settings = new TutorSettings { Id = 1, Instructions = TutorInstructions.Default };
        db.TutorSettings.Add(settings);
        await db.SaveChangesAsync(ct);

        return settings;
    }

    /// <summary>What is actually sent: the standing instructions, then the live state.</summary>
    public async Task<string> InstructionsAsync(CancellationToken ct)
    {
        var (standing, state) = await PartsAsync(ct);
        return Join(standing, state);
    }

    /// <summary>
    /// The same payload, kept apart. One half is written by hand and yours to edit; the other is
    /// read from the trainers and the course tables and cannot be typed over. Showing them as one
    /// block hides which is which, and only one of them is worth editing.
    /// </summary>
    public async Task<(string Standing, string State)> PartsAsync(CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var state = await courseState.BuildAsync(ct);

        return (settings.Instructions, state);
    }

    public static string Join(string standing, string state) =>
        string.Join("\n\n---\n\n", standing, state);

    /// <summary>Rewrites the standing instructions. Empty restores the built-in text.</summary>
    public async Task<string> SetInstructionsAsync(string? text, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);

        settings.Instructions = text?.Trim() is { Length: > 0 } written
            ? written
            : TutorInstructions.Default;

        await db.SaveChangesAsync(ct);
        return settings.Instructions;
    }

    public async Task<TutorTurn> SendAsync(string? text, CancellationToken ct)
    {
        var message = (text ?? "").Trim();
        if (message.Length == 0) throw new AiException("Nothing to send.");

        await budget.RequireHeadroomAsync(ct);

        // Saved first, and on purpose: if OpenAI is down, the typing is still here.
        var question = new ChatMessage { Role = ChatRole.User, Content = message };
        db.ChatMessages.Add(question);
        await db.SaveChangesAsync(ct);

        var settings = await SettingsAsync(ct);
        var instructions = await InstructionsAsync(ct);
        var started = Stopwatch.StartNew();

        try
        {
            var reply = await client.SendAsync(instructions, message, settings.ConversationRef, ct);

            var answer = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = reply.Text,
                ResponseRef = reply.ResponseId,
                Model = reply.Model,
                InputTokens = reply.Usage.InputTokens,
                OutputTokens = reply.Usage.OutputTokens,
                LatencyMs = (int)started.ElapsedMilliseconds,
            };

            db.ChatMessages.Add(answer);
            Continue(settings, reply.ResponseId);
            await db.SaveChangesAsync(ct);

            return new TutorTurn(question, answer);
        }
        catch (Exception ex)
        {
            question.Failed = true;
            question.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(ct);

            logger.LogError(ex, "Tutor request failed after {Elapsed}ms", started.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// The streamed form. The caller writes each delta out as it arrives and calls
    /// <see cref="CompleteAsync"/> once the stream ends — including when it ends badly, so a
    /// half-finished answer is still recorded rather than lost.
    /// </summary>
    public async Task<ChatMessage> BeginAsync(string? text, CancellationToken ct)
    {
        var message = (text ?? "").Trim();
        if (message.Length == 0) throw new AiException("Nothing to send.");

        await budget.RequireHeadroomAsync(ct);

        var question = new ChatMessage { Role = ChatRole.User, Content = message };
        db.ChatMessages.Add(question);
        await db.SaveChangesAsync(ct);

        return question;
    }

    public IAsyncEnumerable<AiChunk> StreamAsync(string instructions, string message, string? previous, CancellationToken ct) =>
        client.StreamAsync(instructions, message, previous, ct);

    public async Task CompleteAsync(
        ChatMessage question, string text, AiChunk final, int latencyMs, string? error, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);

        if (error is not null)
        {
            question.Failed = true;
            question.ErrorMessage = error;
        }

        // Even a stream that broke leaves what it managed to say
        if (text.Length > 0)
        {
            db.ChatMessages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = text,
                ResponseRef = final.ResponseId,
                InputTokens = final.Usage?.InputTokens ?? 0,
                OutputTokens = final.Usage?.OutputTokens ?? 0,
                LatencyMs = latencyMs,
                Failed = error is not null,
                ErrorMessage = error,
            });
        }

        if (final.ResponseId is { Length: > 0 }) Continue(settings, final.ResponseId);
        await db.SaveChangesAsync(ct);
    }

    private static void Continue(TutorSettings settings, string responseId)
    {
        settings.ConversationStartedAt ??= DateTime.UtcNow;
        settings.ConversationRef = responseId;
        settings.LastUsedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Start again: the chat is cleared from view and OpenAI's thread is forgotten. Everything
    /// learned stays — lessons, grammar, weak points, vocabulary and every score are in their own
    /// tables and were never part of the conversation.
    /// </summary>
    public async Task<int> NewConversationAsync(CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        settings.ConversationRef = null;
        settings.ConversationStartedAt = null;

        var cleared = await db.ChatMessages
            .Where(m => !m.Hidden)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.Hidden, true), ct);

        await db.SaveChangesAsync(ct);
        return cleared;
    }

    public Task<List<ChatMessage>> HistoryAsync(int take, CancellationToken ct) =>
        db.ChatMessages
            .Where(m => !m.Hidden)
            .AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Take(take)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
}
