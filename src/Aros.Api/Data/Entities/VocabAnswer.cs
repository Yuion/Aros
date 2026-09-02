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
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;

    public VocabWord VocabWord { get; set; } = null!;
}
