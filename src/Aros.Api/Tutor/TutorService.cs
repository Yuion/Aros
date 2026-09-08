using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

/// <summary>
/// Sending a message and keeping the record of it. Two rules shape the whole class:
///
/// the typed message is saved *before* the request leaves, so a failure never eats it; and the
/// course state is rebuilt on every send, so the tutor is never working from a stale copy of what
/// the learner knows.
/// </summary>
public class TutorService(AppDbContext db, CourseState courseState, LessonRuntimeService runtimeService)
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
        var (standing, state, runtime) = await PartsAsync(ct);
        return Join(standing, state, runtime);
    }

    /// <summary>
    /// The same payload, kept apart. One half is written by hand and yours to edit; the other is
    /// read from the trainers and the course tables and cannot be typed over. Showing them as one
    /// block hides which is which, and only one of them is worth editing.
    /// </summary>
    public async Task<(string Standing, string State, string Runtime)> PartsAsync(CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var state = await courseState.BuildAsync(ct);
        var runtime = LessonRuntimeService.Describe(await runtimeService.CurrentAsync(ct));

        return (settings.Instructions, state, runtime);
    }

    public static string Join(string standing, string state, string runtime) =>
        string.Join("\n\n", standing, state, runtime);

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

    /// <summary>
    /// Start again: the chat is cleared, OpenAI's thread is forgotten and the lesson runtime is
    /// reset, so nothing is left pending from a lesson that is over. Everything
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
        await runtimeService.ResetAsync(ct);

        return cleared;
    }

    public Task<List<ChatMessage>> HistoryAsync(int take, CancellationToken ct) =>
        db.ChatMessages
            .Where(m => !m.Hidden)
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
}
