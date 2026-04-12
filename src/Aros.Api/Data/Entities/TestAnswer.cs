namespace Aros.Api.Data.Entities;

public class TestAnswer
{
    public int Id { get; set; }
    public int TestSessionId { get; set; }
    public int VocabEntryId { get; set; }
    public TestType TestType { get; set; }
    public bool Correct { get; set; }           // Overall correct
    public int TimeTakenMs { get; set; }

    // KanjiToRomaji: tracks two components separately
    public bool? RomajiCorrect { get; set; }
    public bool? TranslationCorrect { get; set; }

    // EnglishToKanjiMultiple: which entry the user picked (may differ from VocabEntryId)
    public int? SelectedVocabEntryId { get; set; }

    public TestSession TestSession { get; set; } = null!;
    public VocabEntry VocabEntry { get; set; } = null!;
    public VocabEntry? SelectedVocabEntry { get; set; }
}
