namespace Aros.Api.Data.Entities;

/// <summary>
/// The one row that holds where the course stands and which OpenAI conversation is running.
/// Settings rather than configuration: all of it changes while the app is running.
/// </summary>
public class TutorSettings
{
    public int Id { get; set; }                       // always 1

    /// <summary>The last response to chain from, or null to begin a fresh conversation.</summary>
    public string? ConversationRef { get; set; }

    /// <summary>Meaning of the stored state, not the shape of the tables. See the plan, §20.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Editable without a deploy — the tutor's standing instructions.</summary>
    public string Instructions { get; set; } = "";

    public string Level { get; set; } = "beginner";
    public string CurrentLessonTopic { get; set; } = "";
    public string NextRecommendedTopic { get; set; } = "";

    /// <summary>Everything from the plan's preferences block that has no home of its own.</summary>
    public string PreferencesJson { get; set; } = "{}";

    public DateTime? ConversationStartedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public enum ChatRole { User = 0, Assistant = 1 }

/// <summary>
/// One turn of the tutor chat. Kept for the page to redraw and for the cost it reports; the
/// learning state is never read from here — that is what the trainers and the tutor tables are for.
/// </summary>
public class ChatMessage
{
    public int Id { get; set; }
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set on the assistant turn: what answered, how much it cost, how long it took.</summary>
    public string? ResponseRef { get; set; }
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }

    /// <summary>A question that never got an answer. Kept so the typed text is never lost.</summary>
    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum GrammarStatus { Introduced = 0, Learned = 1, Shaky = 2 }

public class GrammarPoint
{
    public int Id { get; set; }
    public string Key { get; set; } = "";             // "yes_no_ma", unique
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public GrammarStatus Status { get; set; }
    public int? IntroducedInLesson { get; set; }
    public List<string> Examples { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Tone sandhi and the like — rules taught, as distinct from words and patterns.</summary>
public class PronunciationRule
{
    public int Id { get; set; }
    public string Key { get; set; } = "";             // "bu_sandhi", unique
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public int? IntroducedInLesson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum WeakPointKind { Word = 0, Grammar = 1, Pronunciation = 2, Other = 3 }

/// <summary>
/// A weakness the tutor observed, which no trainer can measure — a tone missed aloud, a pattern
/// misused in free writing. Measured weakness stays where it is measured: VocabProgress and
/// TtsClipStat own that, and this never writes to them.
/// </summary>
public class WeakPoint
{
    public int Id { get; set; }
    public string Target { get; set; } = "";          // "高" or "yes_no_ma"
    public WeakPointKind Kind { get; set; }
    public string Type { get; set; } = "";            // "tone_recall"
    public string Expected { get; set; } = "";
    public int Severity { get; set; } = 1;            // 1-3
    public string? Notes { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class Lesson
{
    public int Id { get; set; }
    public int Number { get; set; }
    public DateOnly Date { get; set; }
    public int? DurationMinutes { get; set; }
    public string Summary { get; set; } = "";
    public string NextRecommendedTopic { get; set; } = "";

    // Recorded as written rather than as foreign keys: a lesson is a historical note, and it should
    // still read correctly after a word it introduced has been deleted from the pool.
    public List<string> NewVocabulary { get; set; } = [];
    public List<string> NewGrammar { get; set; } = [];
    public List<string> Reinforced { get; set; } = [];
    public string MistakeNotes { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ProposalStatus { Pending = 0, Applied = 1, Rejected = 2 }

/// <summary>
/// A change the tutor proposes to the course state, written but not yet applied. Everything the
/// model wants to record passes through here: a year of practice history is worth one click of
/// confirmation, and a summary that misreads the lesson should be easy to throw away.
///
/// The payload is the same JSON shape as the course file, so applying a proposal is the importer
/// that already exists rather than a second path into the database.
/// </summary>
public class TutorProposal
{
    public int Id { get; set; }
    public ProposalStatus Status { get; set; }

    /// <summary>The course-file JSON this would import.</summary>
    public string Payload { get; set; } = "";

    /// <summary>What it says in a sentence, for the approval screen.</summary>
    public string Summary { get; set; } = "";

    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}
