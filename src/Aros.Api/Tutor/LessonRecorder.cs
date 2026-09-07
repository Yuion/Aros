using System.Text.Json;
using System.Text.Json.Nodes;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Tutor;

/// <summary>
/// Turns the lesson that just happened into a course-state update, without you writing it.
///
/// It is a second call rather than a tool the model may or may not decide to invoke: the request
/// is made when you say the lesson is over, so it always happens, and the reply is judged as data
/// rather than trusted as an action. The model still sees the whole lesson — the conversation is
/// held server-side and chained by previous_response_id — so it summarises from the real thing.
///
/// What comes back is stored as a proposal and applied only when you say so. The payload is the
/// same shape as the course file, so applying it is <see cref="CourseImporter"/> and not a second
/// way into the database.
/// </summary>
public class LessonRecorder(AppDbContext db, OpenAiClient client, ILogger<LessonRecorder> logger)
{
    // The shape lives in LessonSchema and is enforced by the API. What is left here is judgement,
    // which no schema can express: what counts as new, and what counts as a weakness.
    private const string Request =
        """
        The lesson is over. Write it up.

        - Base it only on what actually happened in this conversation.
        - Grammar and pronunciation rules: only what was ACTUALLY INTRODUCED OR CORRECTED today.
          Do not restate what was already known. An empty array is the right answer when nothing
          new came up, and is expected more often than not.
        - A weak point needs evidence from today: a repeated mistake, or the learner saying so.
          One slip of attention is not a weakness. Empty is normal.
        - resolved_weak_points names existing weaknesses answered correctly and without hesitation
          today. Leave it empty if unsure.
        - new_vocabulary lists characters only. The words themselves are imported separately from
          your vocabulary table, with their pinyin and meanings.
        - This lesson is number LESSON_NUMBER and today is TODAY.
        """;

    public async Task<TutorProposal> RecordAsync(CancellationToken ct)
    {
        var settings = await db.TutorSettings.FirstOrDefaultAsync(ct)
            ?? throw new AiException("The tutor has not been used yet.");

        if (settings.ConversationRef is not { Length: > 0 })
            throw new AiException("There is no lesson in progress to write up. Say something first.");

        var lessonNumber = (await db.Lessons.MaxAsync(l => (int?)l.Number, ct) ?? 0) + 1;

        var request = Request
            .Replace("LESSON_NUMBER", lessonNumber.ToString())
            .Replace("TODAY", DateTime.Now.ToString("yyyy-MM-dd"));

        // The shape is enforced by the API, not merely requested: with a schema attached the model
        // cannot return prose, a code fence, or a field that is missing or misspelled.
        var reply = await client.SendAsync(
            "You are writing a structured record of a Mandarin lesson.",
            request,
            settings.ConversationRef,
            ct,
            LessonSchema.Definition);

        var payload = Extract(reply.Text);

        var proposal = new TutorProposal
        {
            Payload = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Summary = Describe(payload, lessonNumber),
            Model = reply.Model,
            InputTokens = reply.Usage.InputTokens,
            OutputTokens = reply.Usage.OutputTokens,
        };

        db.TutorProposals.Add(proposal);

        // The write-up is a turn of the conversation, so the chain moves on with it
        settings.ConversationRef = reply.ResponseId;
        settings.LastUsedAt = DateTime.UtcNow;

        // Counted against the daily budget like any other call
        db.ChatMessages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "(lesson written up)",
            ResponseRef = reply.ResponseId,
            Model = reply.Model,
            InputTokens = reply.Usage.InputTokens,
            OutputTokens = reply.Usage.OutputTokens,
        });

        await db.SaveChangesAsync(ct);
        return proposal;
    }

    /// <summary>
    /// Models wrap JSON in prose or a fence however firmly they are told not to, so the object is
    /// taken from the first brace to the last rather than assumed to be the whole reply.
    /// </summary>
    private JsonNode Extract(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            logger.LogError("Lesson write-up was not JSON: {Text}", Trim(text));
            throw new AiException("The tutor did not answer with JSON. Nothing was saved; try again.");
        }

        try
        {
            return JsonNode.Parse(text[start..(end + 1)])
                   ?? throw new AiException("The tutor's write-up was empty.");
        }
        catch (JsonException ex)
        {
            logger.LogError("Lesson write-up did not parse: {Message}. Text: {Text}", ex.Message, Trim(text));
            throw new AiException($"The tutor's write-up was not valid JSON ({ex.Message}). Nothing was saved.");
        }
    }

    private static string Trim(string text) => text.Length > 500 ? text[..500] + "…" : text;

    /// <summary>One line for the approval screen, so what is about to be written is visible.</summary>
    private static string Describe(JsonNode payload, int lessonNumber)
    {
        var parts = new List<string> { $"lesson {lessonNumber}" };

        Count(payload, "grammar", "grammar point", parts);
        Count(payload, "pronunciation_rules", "pronunciation rule", parts);
        Count(payload, "weak_points", "weak point", parts);
        Count(payload, "resolved_weak_points", "weak point resolved", parts);

        return string.Join(", ", parts);
    }

    private static void Count(JsonNode payload, string field, string noun, List<string> parts)
    {
        var count = payload[field]?.AsArray().Count ?? 0;
        if (count > 0) parts.Add($"{count} {noun}{(count == 1 ? "" : "s")}");
    }
}
