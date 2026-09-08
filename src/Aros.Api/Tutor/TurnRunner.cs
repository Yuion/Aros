using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

public record TurnResult(ChatMessage Question, ChatMessage Answer, Exercise? Exercise, string? Warning);

/// <summary>
/// One turn of a lesson, start to finish.
///
/// The division of labour is the point. The model chooses the action, writes the teaching and
/// supplies the answer it has in mind. The application decides identity, builds the character bank
/// from those answers, refuses a repeat, and moves the runtime state on. Everything a model is
/// unreliable at is arithmetic here, and everything arithmetic cannot do is left to the model.
/// </summary>
public class TurnRunner(
    AppDbContext db,
    OpenAiClient client,
    CourseState courseState,
    LessonRuntimeService runtimeService,
    ExerciseGuard guard,
    AiBudget budget,
    ILogger<TurnRunner> logger)
{
    public async Task<TurnResult> RunAsync(string? text, CancellationToken ct)
    {
        var message = (text ?? "").Trim();
        if (message.Length == 0) throw new AiException("Nothing to send.");

        await budget.RequireHeadroomAsync(ct);

        var settings = await SettingsAsync(ct);
        var runtime = await runtimeService.CurrentAsync(ct);

        // Recorded before the model is consulted: if an exercise was pending, this message is the
        // answer to it, and that is a fact rather than something to be inferred from the text.
        await runtimeService.MarkAnsweringAsync(runtime, ct);

        var question = new ChatMessage { Role = ChatRole.User, Content = message };
        db.ChatMessages.Add(question);
        await db.SaveChangesAsync(ct);

        var started = Stopwatch.StartNew();

        try
        {
            var (turn, reply) = await AskAsync(settings, runtime, message, ct);

            var exercise = await BuildExerciseAsync(turn, runtime, ct);
            var warning = exercise?.Warning;

            var answer = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = turn["message"]?.GetValue<string>() ?? "",
                ResponseRef = reply.ResponseId,
                Model = reply.Model,
                InputTokens = reply.Usage.InputTokens,
                OutputTokens = reply.Usage.OutputTokens,
                LatencyMs = (int)started.ElapsedMilliseconds,
            };

            db.ChatMessages.Add(answer);

            // The exercise is a turn of its own, so it stays in the thread rather than living in a
            // panel that can only ever hold the latest one.
            if (exercise?.Exercise is { } set)
            {
                db.ChatMessages.Add(new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = "",
                    ExerciseKey = set.Key,
                    Model = reply.Model,
                });
            }

            settings.ConversationStartedAt ??= DateTime.UtcNow;
            settings.ConversationRef = reply.ResponseId;
            settings.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await ApplyAsync(turn, runtime, exercise?.Exercise, ct);

            return new TurnResult(question, answer, exercise?.Exercise, warning);
        }
        catch (Exception ex)
        {
            question.Failed = true;
            question.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(ct);

            logger.LogError(ex, "Tutor turn failed after {Elapsed}ms", started.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<(JsonNode Turn, AiReply Reply)> AskAsync(
        TutorSettings settings, LessonRuntime runtime, string message, CancellationToken ct)
    {
        var instructions = await InstructionsAsync(ct);

        // Grading needs the answers the model had in mind. Relying on it to remember them across a
        // long thread is the kind of recall this whole design exists to stop depending on.
        if (await PendingAsync(runtime, ct) is { } pending)
        {
            var describe = PendingExercise.Describe(CharacterBank.Read(pending.ItemsJson), message, pending.Key);
            instructions = string.Join("\n\n", instructions, describe);
        }

        var reply = await client.SendAsync(instructions, message, settings.ConversationRef, ct, TurnSchema.Definition);

        return (Parse(reply.Text), reply);
    }

    private async Task<Exercise?> PendingAsync(LessonRuntime runtime, CancellationToken ct) =>
        runtime.AnsweringExerciseKey is { Length: > 0 } key
            ? await db.Exercises.AsNoTracking().FirstOrDefaultAsync(e => e.Key == key, ct)
            : null;

    private record BuiltExercise(Exercise? Exercise, string? Warning);

    /// <summary>
    /// Turns the model's items into an exercise the application owns — or refuses them. A repeat
    /// is dropped rather than shown, and a bank that would be missing a character is reported
    /// rather than sent, because an unsolvable exercise reads to the learner as their own failure.
    /// </summary>
    private async Task<BuiltExercise?> BuildExerciseAsync(JsonNode turn, LessonRuntime runtime, CancellationToken ct)
    {
        if (turn["exercise"] is not JsonObject proposed) return null;

        var items = (proposed["items"]?.AsArray() ?? [])
            .Select(i => new ExerciseItem(
                i?["prompt"]?.GetValue<string>() ?? "",
                i?["expected_answer"]?.GetValue<string>() ?? ""))
            .Where(i => i.Prompt.Length > 0)
            .ToList();

        if (items.Count == 0) return null;

        var fingerprint = ExerciseGuard.Fingerprint(items);

        // A repeat gets no second identity, but dropping it silently loses the task: asking for the
        // same drill again is a fair request. The one already stored is reopened and shown afresh.
        if (await guard.DuplicateOfAsync(fingerprint, ct) is { } repeat)
        {
            var again = await db.Exercises.FirstAsync(e => e.Key == repeat.Key, ct);
            again.SentAt = DateTime.UtcNow;
            again.AnsweredAt = null;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Exercise {Key} set again rather than duplicated", again.Key);
            return new BuiltExercise(again, $"{again.Key} is the same task, so it was set again rather than duplicated.");
        }

        var known = CharacterBank.KnownCharacters(
            await db.VocabWords.AsNoTracking().Where(w => !w.NeedsReview && w.Active).ToListAsync(ct));

        var bank = CharacterBank.Build(items, known);
        var missing = CharacterBank.MissingFrom(bank, items);

        var exercise = new Exercise
        {
            Key = await guard.NextKeyAsync(runtime.LessonId, ct),
            LessonId = runtime.LessonId,
            Type = proposed["type"]?.GetValue<string>() ?? "",
            Instructions = proposed["instructions"]?.GetValue<string>() ?? "",
            ItemsJson = CharacterBank.Write(items),
            Fingerprint = fingerprint,
            // Only Chinese-production tasks get a bank; the others would be a giveaway
            CharacterBank = NeedsBank(proposed["type"]?.GetValue<string>(), items) ? bank : [],
        };

        db.Exercises.Add(exercise);
        await db.SaveChangesAsync(ct);

        return new BuiltExercise(exercise,
            missing.Count > 0 ? $"The expected answers use characters outside the bank: {string.Join(" ", missing)}." : null);
    }

    /// <summary>
    /// A bank belongs on tasks where Chinese must be produced. Handing one to a
    /// Chinese-to-English or pinyin-only task would answer the question.
    /// </summary>
    private static bool NeedsBank(string? type, IEnumerable<ExerciseItem> items)
    {
        var kind = (type ?? "").ToLowerInvariant();

        if (kind.Contains("to_english") || kind.Contains("pinyin") || kind.Contains("tone")) return false;

        // Falls back to the answers themselves: if they are Chinese, the learner must write Chinese
        return items.Any(i => i.ExpectedAnswer.Any(c => c >= 0x4E00 && c <= 0x9FFF));
    }

    private async Task ApplyAsync(JsonNode turn, LessonRuntime runtime, Exercise? exercise, CancellationToken ct)
    {
        var action = turn["action"]?.GetValue<string>() ?? "";

        if (action == "GRADE_PENDING_EXERCISE" || runtime.AnsweringExerciseKey is { Length: > 0 })
            await runtimeService.GradedAsync(runtime, ct);

        if (exercise is not null) await runtimeService.ExerciseSentAsync(runtime, exercise, ct);

        await runtimeService.NotedAsync(
            runtime,
            Strings(turn["new_vocabulary_this_turn"]),
            Strings(turn["new_grammar_this_turn"]),
            ct);

        if (turn["lesson_complete"]?.GetValue<bool>() == true)
        {
            runtime.Phase = LessonPhase.Idle;
            await db.SaveChangesAsync(ct);
        }
    }

    private static IEnumerable<string> Strings(JsonNode? node) =>
        (node?.AsArray() ?? []).Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0);

    /// <summary>The whole payload: how to teach, who the learner is, and what is happening now.</summary>
    public async Task<string> InstructionsAsync(CancellationToken ct)
    {
        var (standing, state, runtime) = await PartsAsync(ct);
        return string.Join("\n\n", standing, state, runtime);
    }

    public async Task<(string Standing, string State, string Runtime)> PartsAsync(CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        var state = await courseState.BuildAsync(ct);
        var runtime = LessonRuntimeService.Describe(await runtimeService.CurrentAsync(ct));

        return (settings.Instructions, state, runtime);
    }

    private async Task<TutorSettings> SettingsAsync(CancellationToken ct)
    {
        var settings = await db.TutorSettings.FirstOrDefaultAsync(ct);
        if (settings is not null) return settings;

        settings = new TutorSettings { Id = 1, Instructions = TutorInstructions.Default };
        db.TutorSettings.Add(settings);
        await db.SaveChangesAsync(ct);

        return settings;
    }

    private static JsonNode Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
            throw new AiException("The tutor did not answer in the expected form. Nothing was saved; try again.");

        try
        {
            return JsonNode.Parse(text[start..(end + 1)]) ?? throw new AiException("The tutor's reply was empty.");
        }
        catch (JsonException ex)
        {
            throw new AiException($"The tutor's reply did not parse ({ex.Message}).");
        }
    }
}
