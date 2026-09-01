namespace Aros.Api.Data.Entities;

/// <summary>
/// A set of characters that sound identical (他 / 她 / 它 — all "tā"). Sentences that differ
/// only inside such groups are indistinguishable by ear, so the listening game never offers
/// two of them in the same question: that would be a coin flip, not a comprehension test.
/// </summary>
public class HomophoneGroup
{
    public int Id { get; set; }
    public string Characters { get; set; } = "";      // e.g. "他她它" — deduplicated, no separators
    public string? Reading { get; set; }              // e.g. "tā" — a label for the list, not used in matching
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
