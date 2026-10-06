using System.Text.Json.Nodes;

namespace Aros.Api.Tutor;

/// <summary>
/// The shape of one tutor turn, enforced by the API.
///
/// The model decides one action and writes the prose. It does *not* write the character bank, the
/// exercise identity, or the check that this task has not been set before — those are mechanical,
/// and the application does them from the expected answers. What the model supplies that nothing
/// else can is the pedagogy: what to teach next, and what the right answer is.
/// </summary>
public static class TurnSchema
{
    public static readonly string[] Actions =
    [
        "GRADE_PENDING_EXERCISE",
        "TEACH_NEXT_INPUT",
        "SEND_NEXT_EXERCISE",
        "SEND_REINFORCEMENT",
        "END_LESSON",
        "ANSWER_USER_QUESTION",
    ];

    /// <summary>
    /// The shapes an exercise can take. Left as free text, every lesson became English sentences
    /// to translate: of 47 exercises set before this list existed, all but one were
    /// english_to_chinese or pinyin. A closed list is the only thing that makes the other shapes
    /// visible to the model at the moment it chooses one.
    ///
    /// error_correction was removed. In practice the instruction line gave the fault away - "each
    /// sentence is wrong because it uses 了 after 没有" leaves nothing to find, only a deletion to
    /// perform - and a version that withheld it would be a guessing game instead. Seven were set;
    /// none of them taught anything.
    /// </summary>
    public static readonly string[] ExerciseTypes =
    [
        "english_to_chinese",       // produce the sentence
        "chinese_to_english",       // show you understood it
        "pinyin",                   // write the reading, tones included
        "tone",                     // the tones alone, for a sentence already read
        "transformation",           // statement to question, positive to negative, add 也
        "constrained",              // say it again, but using 要 / without 想
        "answer_in_chinese",        // a question asked in Chinese, answered in Chinese
    ];

    /// <summary>What a conversation turn may be. Nothing here can set an exercise.</summary>
    public static readonly string[] TalkActions =
    [
        "ANSWER_USER_QUESTION",
        "EXPLAIN",
    ];

    /// <summary>What a text turn may be: write one, mark a translation of one, or answer about one.</summary>
    public static readonly string[] TextActions =
    [
        "SEND_TEXT",
        "MARK_TRANSLATION",
        "ANSWER_USER_QUESTION",
    ];

    public static JsonSchema Definition { get; } = new("tutor_turn", Build());

    private static readonly JsonSchema TalkDefinition = new("tutor_talk", BuildTalk());
    private static readonly JsonSchema TextDefinition = new("tutor_text", BuildText());

    /// <summary>
    /// The reply shape for the job in hand.
    ///
    /// Asking a model not to set exercises while handing it a field for one is a request it will
    /// eventually decline — and it did: a conversation turned into drills within three messages.
    /// With no exercise field in the schema there is nothing to decline.
    /// </summary>
    public static JsonSchema For(Data.Entities.TutorMode mode) => mode switch
    {
        Data.Entities.TutorMode.Talk => TalkDefinition,
        Data.Entities.TutorMode.Text => TextDefinition,
        _ => Definition,
    };

    private static JsonObject BuildTalk() => Object(new JsonObject
    {
        ["action"] = Enum(TalkActions, "Exactly one action for this turn."),

        ["message"] = Text(
            "The whole reply, in markdown. This is a conversation: answer what was asked, as "
            + "fully as it deserves and no further. No exercises, no drills, no homework."),

        ["new_vocabulary_this_turn"] = Array(
            Text("Characters introduced in this message, if any. Usually none in a conversation.")),

        ["new_grammar_this_turn"] = Array(
            Text("Grammar patterns introduced in this message, if any.")),
    });

    private static JsonObject BuildText() => Object(new JsonObject
    {
        ["action"] = Enum(TextActions, "Exactly one action for this turn."),

        ["message"] = Text(
            "What the learner reads, in markdown. When sending a text, this is a line or two of "
            + "framing at most — never the text itself, never its translation, and never its "
            + "reading: those are fields, and the application renders them so the translation "
            + "stays hidden until it is asked for. When marking, this is the marking."),

        ["text"] = Nullable(Object(new JsonObject
        {
            ["title"] = Text("A few words naming the text, in English."),
            ["chinese"] = Text(
                "The text itself, in characters, with normal punctuation. One coherent passage — "
                + "a story, an account of a day, a letter — not a list of unrelated sentences."),
            ["pinyin"] = Text(
                "The whole text's reading in tone numbers, spoken tones with sandhi, sentence by "
                + "sentence in the same order."),
            ["english"] = Text(
                "Your own translation of the text. The learner does not see this until they ask "
                + "for it, so it is the answer key: make it a faithful translation rather than a "
                + "paraphrase."),
            ["notes"] = Text(
                "Anything worth knowing before starting: a construction that will trip them up, "
                + "a turn of phrase. Empty string when there is nothing to say."),
            ["new_words"] = Array(Object(new JsonObject
            {
                ["characters"] = Text("The word, in characters."),
                ["pinyin"] = Text(
                    "Its dictionary reading in tone numbers, syllables spaced: xian4 zai4. This "
                    + "is what the learner will be drilled on, so it must be the standard "
                    + "reading of the word on its own, not the sandhi form it takes in the text."),
                ["english"] = Text("What it means. Alternatives separated by a slash."),
            })),
            ["words_used"] = Array(Text("Vocabulary from the learner's own list that this text uses.")),
            ["grammar_used"] = Array(Text("Grammar patterns from the learner's own list that this text uses.")),
        }), "The text being set this turn, or null when not setting one."),

        ["new_vocabulary_this_turn"] = Array(
            Text("Characters introduced in this message that the learner did not already have.")),

        ["new_grammar_this_turn"] = Array(
            Text("Grammar patterns introduced in this message, if any.")),
    });

    private static JsonObject Build() => Object(new JsonObject
    {
        ["action"] = Enum(Actions, "Exactly one action for this turn."),

        ["message"] = Text(
            "What the learner reads, in markdown. Teaching, grading and explanation go here. Do "
            + "NOT write the exercise items or a character bank here — those are rendered by the "
            + "application from the exercise field."),

        ["exercise"] = Nullable(Object(new JsonObject
        {
            ["type"] = Enum(
                ExerciseTypes,
                "The shape of this exercise. Vary it: a lesson of nothing but english_to_chinese "
                + "tests one skill five times."),
            ["instructions"] = Text("One line telling the learner what to do."),
            ["items"] = Array(Object(new JsonObject
            {
                ["prompt"] = Text("What the learner sees for this item."),
                ["prompt_pinyin"] = NullableText(
                    "The prompt's reading in tone numbers — ta1 zuo4 che1 qu4 ji1 chang3 — whenever "
                    + "the prompt contains Chinese characters. The learner cannot read characters "
                    + "unaided, so a prompt in characters without this is a task they cannot start. "
                    + "Null when the prompt is English, and null for the pinyin and tone types, "
                    + "where the reading is what is being asked for — the application drops it "
                    + "there in any case."),
                ["expected_answer"] = Text(
                    "The answer you have in mind. For a Chinese-production task this must be the "
                    + "full Chinese answer: the application builds the character bank from it, so "
                    + "an incomplete answer produces an unsolvable exercise."),
            })),
        }), "The exercise to set this turn, or null when not setting one."),

        ["new_vocabulary_this_turn"] = Array(
            Text("Characters introduced in this message, if any.")),

        ["new_grammar_this_turn"] = Array(
            Text("Grammar patterns introduced in this message, if any.")),

        ["lesson_plan"] = Nullable(
            Text(
                "One line, only on the first turn of a lesson: what this lesson will cover and "
                + "what it will leave alone. Ignored afterwards — the plan is fixed once set, and "
                + "the write-up reports against it."),
            "The plan for this lesson, or null on every turn after the first."),

        ["lesson_complete"] = Boolean(
            "True only when this turn ends the lesson."),
    });

    // Strict mode: every property required, no extras. Optionality is expressed by nullable types.
    private static JsonObject Object(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray([.. properties.Select(p => (JsonNode)p.Key)]),
        ["additionalProperties"] = false,
    };

    private static JsonObject Nullable(JsonObject shape, string description)
    {
        shape["type"] = new JsonArray(shape["type"]?.GetValue<string>() ?? "object", "null");
        shape["description"] = description;
        return shape;
    }

    private static JsonObject Array(JsonNode items) => new() { ["type"] = "array", ["items"] = items };

    private static JsonObject NullableText(string description) => new()
    {
        ["type"] = new JsonArray("string", "null"),
        ["description"] = description,
    };

    private static JsonObject Text(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    private static JsonObject Boolean(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
    };

    private static JsonObject Enum(string[] values, string description) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]),
        ["description"] = description,
    };
}
