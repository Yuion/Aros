using System.Text;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

/// <summary>
/// Decides whether an exercise is genuinely new, and names it.
///
/// "Do not send the same task twice" is an instruction a model can forget. A fingerprint cannot:
/// the item set is normalised and sorted, so the same two sentences in a different order, with
/// different punctuation or a reworded preamble, are recognised as the same exercise. A duplicate
/// is refused before it reaches the page rather than apologised for afterwards.
/// </summary>
public class ExerciseGuard(AppDbContext db)
{
    /// <summary>How far back a repeat still counts as a repeat.</summary>
    private const int Recent = 12;

    /// <summary>
    /// The item set reduced to what makes it the same task: the expected answers, stripped of
    /// spacing and punctuation, deduplicated and sorted. Wording differences fall away; a genuinely
    /// different set survives.
    /// </summary>
    public static string Fingerprint(IEnumerable<ExerciseItem> items)
    {
        var parts = items
            .Select(i => Normalize(i.ExpectedAnswer))
            .Where(a => a.Length > 0)
            .Distinct()
            .Order(StringComparer.Ordinal);

        return string.Join("|", parts);
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            // Punctuation and spacing carry no identity: 我喝茶。and 我喝茶 are one task
            if (System.Text.Rune.IsLetterOrDigit(rune)) sb.Append(rune);
        }

        return sb.ToString();
    }

    /// <summary>The exercise this repeats, or null when it is new.</summary>
    public async Task<Exercise?> DuplicateOfAsync(string fingerprint, CancellationToken ct)
    {
        if (fingerprint.Length == 0) return null;

        return await db.Exercises
            .AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(Recent)
            .FirstOrDefaultAsync(e => e.Fingerprint == fingerprint, ct);
    }

    /// <summary>L11-E03: the lesson it belongs to, and its place in that lesson.</summary>
    public async Task<string> NextKeyAsync(string lessonId, CancellationToken ct)
    {
        var lessonNumber = (await db.Lessons.MaxAsync(l => (int?)l.Number, ct) ?? 0) + 1;
        var sentThisLesson = await db.Exercises.CountAsync(e => e.LessonId == lessonId, ct);

        return $"L{lessonNumber}-E{sentThisLesson + 1:00}";
    }
}
