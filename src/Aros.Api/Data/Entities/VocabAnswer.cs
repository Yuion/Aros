namespace Aros.Api.Data.Entities;

/// <summary>
/// One answered vocabulary question. Mirrors <see cref="ListeningAnswer"/> so the stats page
/// can report on both on the same shape.
/// </summary>
public class VocabAnswer
{
    public int Id { get; set; }
    public int VocabWordId { get; set; }
    public VocabDirection Direction { get; set; }
    public bool Correct { get; set; }

    /// <summary>
    /// What was actually typed or assembled, trimmed, or null for an answer recorded before this
    /// was kept. Correct/incorrect says a mistake happened; this says what the mistake was, which
    /// is the difference between "ni2 for ni3" and not knowing the word at all. Nothing schedules
    /// on it - it is there to be read, by you and by the tutor.
    /// </summary>
    public string? Given { get; set; }

    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;

    public VocabWord VocabWord { get; set; } = null!;
}
