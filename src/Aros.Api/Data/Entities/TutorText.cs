namespace Aros.Api.Data.Entities;

/// <summary>
/// One long text to translate, written by the tutor out of what the learner already knows.
///
/// Deliberately not a listening sentence. A TtsClip is an item in the listening pool: it gets a
/// score per mode, a place in the schedule and a share of every round. A text of a hundred and
/// fifty characters is not a question to be asked by ear fifteen times, and putting it there
/// would crowd out the sentences that are. So a text keeps its own table, and its audio — when
/// it is asked for — is a file on disk named after its own content, with nothing pointing at it
/// from any trainer.
///
/// Every text produced is kept, because the one thing worse than storing a text nobody wanted is
/// losing the one that was good. Deleting is a button.
/// </summary>
public class TutorText
{
    public int Id { get; set; }

    /// <summary>The session it came out of, as the runtime numbers them.</summary>
    public string LessonId { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>The text itself, in characters. This is what gets translated and what gets spoken.</summary>
    public string Chinese { get; set; } = "";

    /// <summary>The reading, spoken tones with sandhi, in the same order as the text.</summary>
    public string Pinyin { get; set; } = "";

    /// <summary>
    /// The tutor's own translation. Stored with the text and hidden by the page until it is
    /// asked for: a reference translation on screen is not a text you translated.
    /// </summary>
    public string English { get; set; } = "";

    /// <summary>Anything worth saying about the text: a new word in it, a construction to watch.</summary>
    public string Notes { get; set; } = "";

    /// <summary>What the tutor set out to use. The point of the exercise is the coverage.</summary>
    public List<string> WordsUsed { get; set; } = [];
    public List<string> GrammarUsed { get; set; } = [];

    /// <summary>
    /// The audio file, once it has been asked for. Empty until then: speaking a text costs money
    /// and most texts are read once, so it is a button rather than something that happens.
    /// </summary>
    public string AudioLocation { get; set; } = "";
    public DateTime? SpokenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
