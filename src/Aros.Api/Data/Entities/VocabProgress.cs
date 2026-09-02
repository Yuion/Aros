namespace Aros.Api.Data.Entities;

/// <summary>
/// Score for one word in one direction. Recognising 水 as "water" says nothing about producing
/// 水 from "water", so each direction is tracked on its own.
/// </summary>
public class VocabProgress
{
    public int Id { get; set; }
    public int VocabWordId { get; set; }
    public VocabDirection Direction { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int ConsecutiveCorrect { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public VocabWord VocabWord { get; set; } = null!;
}
