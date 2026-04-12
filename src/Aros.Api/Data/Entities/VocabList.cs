namespace Aros.Api.Data.Entities;

public class VocabList
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SourceLanguage { get; set; } = "";
    public string TargetLanguage { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VocabEntry> Entries { get; set; } = [];
    public ICollection<TestSession> TestSessions { get; set; } = [];
}
