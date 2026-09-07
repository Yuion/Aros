using System.Text.Json;
using Aros.Api.Data.Entities;

namespace Aros.Api.Tutor;

public record ExerciseItem(string Prompt, string ExpectedAnswer);

/// <summary>
/// Builds the character bank from the answers the model had in mind, rather than asking the model
/// to remember to include every character it will need.
///
/// The difference is the whole point. "Please include every required character" is a request a
/// probabilistic model can fail; extracting the characters from the expected answers cannot fail,
/// as long as the answer itself is right. A bank missing one character makes an exercise
/// unsolvable, and the learner cannot tell whether they have forgotten something or been handed a
/// broken task.
/// </summary>
public static class CharacterBank
{
    /// <summary>Roughly how many extra known characters to mix in, so the bank is not a solution key.</summary>
    private const int Distractors = 6;

    public static List<string> Build(IEnumerable<ExerciseItem> items, IReadOnlyList<string> knownCharacters)
    {
        // Every character the answers actually need. This is the part that must be exhaustive.
        var required = items
            .SelectMany(i => Han(i.ExpectedAnswer))
            .Distinct()
            .ToList();

        var pool = knownCharacters
            .Where(c => !required.Contains(c))
            .Distinct()
            .ToList();

        var extra = pool
            .OrderBy(_ => Random.Shared.Next())
            .Take(Distractors)
            .ToList();

        return [.. required.Concat(extra).OrderBy(_ => Random.Shared.Next())];
    }

    /// <summary>
    /// What the bank is missing, if anything. Should always be empty — it is checked anyway,
    /// because an exercise that cannot be solved is worse than one that is never sent.
    /// </summary>
    public static List<string> MissingFrom(IEnumerable<string> bank, IEnumerable<ExerciseItem> items)
    {
        var have = bank.ToHashSet();
        return [.. items.SelectMany(i => Han(i.ExpectedAnswer)).Distinct().Where(c => !have.Contains(c))];
    }

    /// <summary>The characters a learner could be expected to use, from what they already know.</summary>
    public static List<string> KnownCharacters(IEnumerable<VocabWord> words) =>
        [.. words.SelectMany(w => Han(w.Characters)).Distinct()];

    private static IEnumerable<string> Han(string text) =>
        text.EnumerateRunes()
            .Where(r => r.Value is >= 0x4E00 and <= 0x9FFF
                        or >= 0x3400 and <= 0x4DBF
                        or >= 0xF900 and <= 0xFAFF)
            .Select(r => r.ToString());

    public static List<ExerciseItem> Read(string json) =>
        JsonSerializer.Deserialize<List<ExerciseItem>>(json) ?? [];

    public static string Write(IEnumerable<ExerciseItem> items) => JsonSerializer.Serialize(items);
}
