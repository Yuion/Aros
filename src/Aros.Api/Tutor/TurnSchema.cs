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

    public static JsonSchema Definition { get; } = new("tutor_turn", Build());

    private static JsonObject Build() => Object(new JsonObject
    {
        ["action"] = Enum(Actions, "Exactly one action for this turn."),

        ["message"] = Text(
            "What the learner reads, in markdown. Teaching, grading and explanation go here. Do "
            + "NOT write the exercise items or a character bank here — those are rendered by the "
            + "application from the exercise field."),

        ["exercise"] = Nullable(Object(new JsonObject
        {
            ["type"] = Text("english_to_chinese, chinese_to_english, pinyin, tone, or similar."),
            ["instructions"] = Text("One line telling the learner what to do."),
            ["items"] = Array(Object(new JsonObject
            {
                ["prompt"] = Text("What the learner sees for this item."),
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
        shape["type"] = new JsonArray("object", "null");
        shape["description"] = description;
        return shape;
    }

    private static JsonObject Array(JsonNode items) => new() { ["type"] = "array", ["items"] = items };

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
