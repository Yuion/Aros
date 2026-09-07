using System.Text.Json.Nodes;

namespace Aros.Api.Tutor;

/// <summary>
/// The shape a lesson write-up must take, enforced by the API rather than asked for in the prompt.
///
/// It mirrors the course file exactly, because a write-up and a hand-written file are applied by
/// the same importer. If this and <see cref="CourseImporter"/> ever disagree, the write-up will
/// import as silence rather than as an error — so they are worth reading together.
///
/// Strict mode has two rules worth remembering: every property must be listed in "required", and
/// "additionalProperties" must be false. So a field that is genuinely optional is expressed as a
/// nullable type, not by leaving it out.
/// </summary>
public static class LessonSchema
{
    public static JsonSchema Definition { get; } = new("lesson_writeup", Build());

    private static JsonObject Build() => Object(
        new JsonObject
        {
            ["course"] = Object(new JsonObject
            {
                ["current_lesson_topic"] = Text("What this lesson covered, one line."),
                ["next_recommended_topic"] = Text("What the next lesson should do, one line."),
            }),

            ["grammar"] = Array(Object(new JsonObject
            {
                ["id"] = Text("Short snake_case identifier, stable across lessons."),
                ["title"] = Text("The pattern, briefly."),
                ["summary"] = Text("One or two sentences."),
                ["status"] = Enum(["introduced", "learned", "shaky"]),
                ["introduced_lesson"] = Number("The lesson number this belongs to."),
                ["examples"] = Array(Text("An example sentence in Chinese.")),
            })),

            ["pronunciation_rules"] = Array(Object(new JsonObject
            {
                ["id"] = Text("Short snake_case identifier."),
                ["title"] = Text("The rule, briefly."),
                ["summary"] = Text("One or two sentences."),
                ["introduced_lesson"] = Number("The lesson number this belongs to."),
            })),

            ["weak_points"] = Array(Object(new JsonObject
            {
                ["target"] = Text("The character, word or pattern."),
                ["kind"] = Enum(["word", "grammar", "pronunciation", "other"]),
                ["type"] = Text("Short label, for example tone_recall."),
                ["expected"] = Text("What the right answer was."),
                ["severity"] = Number("1 to 3."),
                ["notes"] = Text("The evidence from this lesson."),
            })),

            ["resolved_weak_points"] = Array(Text("The target of a weakness that looked solid today.")),

            ["lesson_history"] = Array(Object(new JsonObject
            {
                ["lesson_number"] = Number("This lesson's number."),
                ["date"] = Text("YYYY-MM-DD."),
                ["duration_minutes"] = Nullable("integer", "Minutes, or null if unknown."),
                ["summary"] = Text("What happened, three or four sentences."),
                ["new_vocabulary"] = Array(Text("Characters only, no pinyin or meaning.")),
                ["new_grammar"] = Array(Text("Patterns introduced.")),
                ["reinforced"] = Array(Text("What was practised again.")),
                ["mistakes"] = Array(Text("What went wrong, briefly.")),
                ["next_recommended_topic"] = Text("Same as course.next_recommended_topic."),
            })),
        });

    // Strict mode wants every property required and no extras, so both are applied here rather
    // than remembered at each call site.
    private static JsonObject Object(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray([.. properties.Select(p => (JsonNode)p.Key)]),
        ["additionalProperties"] = false,
    };

    private static JsonObject Array(JsonNode items) => new()
    {
        ["type"] = "array",
        ["items"] = items,
    };

    private static JsonObject Text(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    private static JsonObject Number(string description) => new()
    {
        ["type"] = "integer",
        ["description"] = description,
    };

    private static JsonObject Nullable(string type, string description) => new()
    {
        ["type"] = new JsonArray(type, "null"),
        ["description"] = description,
    };

    private static JsonObject Enum(string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]),
    };
}
