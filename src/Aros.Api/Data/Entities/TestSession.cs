namespace Aros.Api.Data.Entities;

public class TestSession
{
    public int Id { get; set; }
    public int VocabListId { get; set; }
    public TestType TestType { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;

    public VocabList VocabList { get; set; } = null!;
    public ICollection<TestAnswer> Answers { get; set; } = [];

    // Computed helpers
    public int TotalQuestions => Answers.Count;
    public int CorrectCount => Answers.Count(a => a.Correct);
}
