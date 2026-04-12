namespace Aros.Api.Data.Entities;

public class VocabEntry
{
    public int Id { get; set; }
    public int VocabListId { get; set; }
    public string SourceWord { get; set; } = "";
    public string TargetWord { get; set; } = "";      // Kanji or Kana (e.g. 今日は / こんにちは)
    public string? TargetReading { get; set; }        // Hiragana reading when TargetWord has Kanji (furigana)
    public string? TargetRomaji { get; set; }         // Romaji (e.g. konnichiwa)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public VocabList VocabList { get; set; } = null!;
    public ICollection<TestAnswer> TestAnswers { get; set; } = [];
}
