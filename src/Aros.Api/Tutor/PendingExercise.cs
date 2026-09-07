using System.Text;

namespace Aros.Api.Tutor;

/// <summary>
/// What the model is told when it is grading: the exercise it set, the answers it had in mind, and
/// — for each one — whether the learner's message literally contains it.
///
/// The comparison is the application's, not the model's. It settles the one case a model should
/// never get wrong and sometimes does: marking an answer wrong and then offering the same answer
/// as the correction. Where the expected string is present character for character, that is a
/// fact, and the instructions say so.
///
/// It does not decide the grade. An answer can be right without matching — a different word order,
/// a synonym, a valid alternative — and judging that is exactly what the model is for. This only
/// removes the case where judgement is not required.
/// </summary>
public static class PendingExercise
{
    public static string Describe(IReadOnlyList<ExerciseItem> items, string learnerMessage, string key)
    {
        if (items.Count == 0) return "";

        var answer = Normalize(learnerMessage);
        var text = new StringBuilder();

        text.AppendLine($"PENDING EXERCISE {key} — the learner's message is their answer to this");

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var expected = Normalize(item.ExpectedAnswer);

            var verdict = expected.Length > 0 && answer.Contains(expected, StringComparison.Ordinal)
                ? "the expected answer appears verbatim in their message — it is correct, do not mark it wrong"
                : "not found verbatim; judge it yourself, it may still be a valid alternative";

            text.AppendLine($"  {i + 1}. {item.Prompt}");
            text.AppendLine($"     expected: {item.ExpectedAnswer}");
            text.AppendLine($"     check: {verdict}");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Spacing and punctuation carry no meaning in a typed answer — the learner pastes characters
    /// after writing them out by hand and leaves the gaps in.
    /// </summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
            if (System.Text.Rune.IsLetterOrDigit(rune)) sb.Append(rune);

        return sb.ToString();
    }
}
