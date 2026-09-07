using System.Text;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

/// <summary>
/// Keeps and renders the runtime state — the half of the context that says what is happening right
/// now rather than what the learner knows.
/// </summary>
public class LessonRuntimeService(AppDbContext db)
{
    public async Task<LessonRuntime> CurrentAsync(CancellationToken ct)
    {
        var runtime = await db.LessonRuntime.FirstOrDefaultAsync(ct);
        if (runtime is not null) return runtime;

        runtime = new LessonRuntime { Id = 1, LessonId = NewLessonId() };
        db.LessonRuntime.Add(runtime);
        await db.SaveChangesAsync(ct);

        return runtime;
    }

    public static string NewLessonId() => $"{DateTime.Now:yyyy-MM-dd}-{DateTime.Now:HHmm}";

    /// <summary>
    /// Written for the model as flat facts rather than prose. This is the block that decides
    /// whether "1. 我想喝茶" is an answer to grade or a request to carry on, so it says which in
    /// as few words as possible.
    /// </summary>
    public static string Describe(LessonRuntime runtime)
    {
        var text = new StringBuilder();
        text.AppendLine("LESSON RUNTIME STATE (authoritative for what to do this turn)");
        text.AppendLine($"  lesson_id: {runtime.LessonId}");
        text.AppendLine($"  phase: {runtime.Phase.ToString().ToLowerInvariant()}");

        if (runtime.CurrentTopic.Length > 0)
            text.AppendLine($"  current_topic: {runtime.CurrentTopic}");

        if (runtime.ExerciseKey is { Length: > 0 })
        {
            text.AppendLine($"  exercise_id: {runtime.ExerciseKey}");
            text.AppendLine($"  exercise_already_sent: {Yes(runtime.ExerciseAlreadySent)}");
            text.AppendLine($"  awaiting_user_answer: {Yes(runtime.AwaitingUserAnswer)}");
        }

        if (runtime.AnsweringExerciseKey is { Length: > 0 })
            text.AppendLine($"  user_is_answering_exercise_id: {runtime.AnsweringExerciseKey}");

        if (runtime.LastCompletedExerciseKey is { Length: > 0 })
            text.AppendLine($"  last_completed_exercise_id: {runtime.LastCompletedExerciseKey}");

        if (runtime.ExercisesSentThisLesson.Count > 0)
            text.AppendLine($"  exercises_sent_this_lesson: {string.Join(", ", runtime.ExercisesSentThisLesson)}");

        if (runtime.NewVocabularyThisLesson.Count > 0)
            text.AppendLine($"  new_vocabulary_this_lesson: {string.Join(" ", runtime.NewVocabularyThisLesson)}");

        if (runtime.NewGrammarThisLesson.Count > 0)
            text.AppendLine($"  new_grammar_this_lesson: {string.Join(", ", runtime.NewGrammarThisLesson)}");

        if (runtime.MinutesRequested is { } minutes)
            text.AppendLine($"  minutes_requested: {minutes}");

        return text.ToString().TrimEnd();
    }

    private static string Yes(bool value) => value ? "true" : "false";

    /// <summary>
    /// The learner has replied while an exercise was pending, so this reply is an answer to it.
    /// Recorded before the model is asked anything, which is what removes the guess.
    /// </summary>
    public async Task MarkAnsweringAsync(LessonRuntime runtime, CancellationToken ct)
    {
        if (!runtime.AwaitingUserAnswer || runtime.ExerciseKey is not { Length: > 0 }) return;

        runtime.AnsweringExerciseKey = runtime.ExerciseKey;
        runtime.AwaitingUserAnswer = false;
        runtime.Phase = LessonPhase.Grading;

        await db.SaveChangesAsync(ct);
    }

    public async Task ExerciseSentAsync(LessonRuntime runtime, Exercise exercise, CancellationToken ct)
    {
        runtime.ExerciseKey = exercise.Key;
        runtime.ExerciseAlreadySent = true;
        runtime.AwaitingUserAnswer = true;
        runtime.AnsweringExerciseKey = null;
        runtime.Phase = LessonPhase.Exercise;

        if (!runtime.ExercisesSentThisLesson.Contains(exercise.Key))
            runtime.ExercisesSentThisLesson = [.. runtime.ExercisesSentThisLesson, exercise.Key];

        await db.SaveChangesAsync(ct);
    }

    public async Task GradedAsync(LessonRuntime runtime, CancellationToken ct)
    {
        runtime.LastCompletedExerciseKey = runtime.AnsweringExerciseKey ?? runtime.ExerciseKey;
        runtime.AnsweringExerciseKey = null;
        runtime.AwaitingUserAnswer = false;

        if (await db.Exercises.FirstOrDefaultAsync(e => e.Key == runtime.LastCompletedExerciseKey, ct) is { } done)
            done.AnsweredAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task NotedAsync(
        LessonRuntime runtime, IEnumerable<string> vocabulary, IEnumerable<string> grammar, CancellationToken ct)
    {
        var words = runtime.NewVocabularyThisLesson.Concat(vocabulary.Where(v => v.Trim().Length > 0)).Distinct().ToList();
        var points = runtime.NewGrammarThisLesson.Concat(grammar.Where(g => g.Trim().Length > 0)).Distinct().ToList();

        if (words.Count == runtime.NewVocabularyThisLesson.Count && points.Count == runtime.NewGrammarThisLesson.Count)
            return;

        runtime.NewVocabularyThisLesson = words;
        runtime.NewGrammarThisLesson = points;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A fresh lesson: a new id, nothing pending, nothing yet introduced.</summary>
    public async Task ResetAsync(CancellationToken ct)
    {
        var runtime = await CurrentAsync(ct);

        runtime.LessonId = NewLessonId();
        runtime.Phase = LessonPhase.Idle;
        runtime.CurrentTopic = "";
        runtime.ExerciseKey = null;
        runtime.ExerciseAlreadySent = false;
        runtime.AwaitingUserAnswer = false;
        runtime.AnsweringExerciseKey = null;
        runtime.LastCompletedExerciseKey = null;
        runtime.ExercisesSentThisLesson = [];
        runtime.NewVocabularyThisLesson = [];
        runtime.NewGrammarThisLesson = [];
        runtime.StartedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
