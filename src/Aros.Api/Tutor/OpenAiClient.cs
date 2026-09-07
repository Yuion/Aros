using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Aros.Api.Tutor;

public record AiUsage(int InputTokens, int OutputTokens);

/// <summary>A schema the model's output must satisfy, enforced by the API rather than requested.</summary>
public record JsonSchema(string Name, JsonObject Definition);

public record AiReply(string ResponseId, string Text, AiUsage Usage, string Model);

/// <summary>One piece of a streamed reply. The last one carries the totals.</summary>
public record AiChunk(string? Delta, string? ResponseId, AiUsage? Usage, bool Done);

public class AiException(string message) : Exception(message)
{
    /// <summary>Whether the same call could plausibly succeed on a second try.</summary>
    public bool Retryable { get; init; }

    /// <summary>The model was rejected by name — worth one attempt at the fallback.</summary>
    public bool UnknownModel { get; init; }
}

/// <summary>
/// The OpenAI Responses API, which is the whole client. Continuity is carried by
/// <c>previous_response_id</c>: each reply's id is stored and handed back on the next call, so the
/// server keeps the transcript and Aros does not have to resend it.
///
/// That is a convenience, not a dependency — the learning state travels in the instructions on
/// every call, so losing the chain costs the last few turns of phrasing and nothing else.
/// </summary>
public class OpenAiClient(HttpClient http, IOptions<AiOptions> options, ILogger<OpenAiClient> logger)
{
    private readonly AiOptions _options = options.Value;

    private const int Attempts = 3;
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

    /// <param name="schema">
    /// When given, the API is told to enforce this JSON Schema on the output rather than merely
    /// being asked for it in the prompt. A described schema is a request; this one is a guarantee.
    /// </param>
    public async Task<AiReply> SendAsync(
        string instructions, string message, string? previousResponseId, CancellationToken ct,
        JsonSchema? schema = null)
    {
        Require();

        var backoff = FirstBackoff;

        for (var attempt = 1; ; attempt++)
        {
            var last = attempt == Attempts;

            try
            {
                return await PostAsync(_options.Model, instructions, message, previousResponseId, ct, schema);
            }
            catch (AiException ex) when (ex.UnknownModel && _options.FallbackModel.Length > 0)
            {
                logger.LogWarning("Model {Model} was rejected; falling back to {Fallback}.",
                    _options.Model, _options.FallbackModel);

                return await PostAsync(_options.FallbackModel, instructions, message, previousResponseId, ct, schema);
            }
            catch (Exception ex) when (!last && IsWorthRetrying(ex, ct))
            {
                logger.LogWarning("OpenAI attempt {Attempt}/{Total} failed: {Message}. Retrying in {Delay}s.",
                    attempt, Attempts, ex.Message, backoff.TotalSeconds);
            }

            await Task.Delay(backoff, ct);
            backoff *= 2;
        }
    }

    /// <summary>
    /// The same call, streamed. Deliberately not retried: once a word has been shown, starting
    /// again would either duplicate it or contradict it. A stream that breaks is reported, and the
    /// caller keeps what arrived.
    /// </summary>
    public async IAsyncEnumerable<AiChunk> StreamAsync(
        string instructions,
        string message,
        string? previousResponseId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Require();

        using var request = Build(_options.Model, instructions, message, previousResponseId, stream: true);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode) throw await FailureAsync(response, ct);

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? responseId = null;
        AiUsage? usage = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payload = line[5..].Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(payload);
            }
            catch (JsonException)
            {
                continue;                       // a keep-alive or a comment line, not an event
            }

            var type = node?["type"]?.GetValue<string>();

            switch (type)
            {
                case "response.output_text.delta":
                    if (node?["delta"]?.GetValue<string>() is { Length: > 0 } delta)
                        yield return new AiChunk(delta, null, null, false);
                    break;

                case "response.created":
                case "response.completed":
                case "response.incomplete":
                    responseId = node?["response"]?["id"]?.GetValue<string>() ?? responseId;
                    usage = ReadUsage(node?["response"]?["usage"]) ?? usage;
                    break;

                case "error":
                case "response.failed":
                    throw new AiException(
                        node?["message"]?.GetValue<string>()
                        ?? node?["response"]?["error"]?["message"]?.GetValue<string>()
                        ?? "The model stopped without finishing.");
            }
        }

        yield return new AiChunk(null, responseId, usage, true);
    }

    private async Task<AiReply> PostAsync(
        string model, string instructions, string message, string? previousResponseId, CancellationToken ct,
        JsonSchema? schema = null)
    {
        using var request = Build(model, instructions, message, previousResponseId, stream: false, schema);
        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode) throw await FailureAsync(response, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var node = JsonNode.Parse(body) ?? throw new AiException("OpenAI returned an empty body.");

        return new AiReply(
            node["id"]?.GetValue<string>() ?? "",
            ExtractText(node),
            ReadUsage(node["usage"]) ?? new AiUsage(0, 0),
            node["model"]?.GetValue<string>() ?? model);
    }

    private HttpRequestMessage Build(
        string model, string instructions, string message, string? previousResponseId, bool stream,
        JsonSchema? schema = null)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["instructions"] = instructions,
            ["input"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = message,
            }),
            ["max_output_tokens"] = _options.MaxOutputTokens,
            ["store"] = true,               // the server keeps the transcript so we need not resend it
        };

        if (previousResponseId is { Length: > 0 }) payload["previous_response_id"] = previousResponseId;
        if (stream) payload["stream"] = true;

        if (schema is not null)
        {
            payload["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = schema.Name,
                    ["strict"] = true,
                    ["schema"] = schema.Definition.DeepClone(),
                },
            };
        }

        // A request message cannot be sent twice, so each attempt builds its own
        var request = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return request;
    }

    /// <summary>
    /// The convenience field when it is there, otherwise walked out of the output array — which is
    /// the shape that is actually guaranteed.
    /// </summary>
    private static string ExtractText(JsonNode node)
    {
        if (node["output_text"]?.GetValue<string>() is { Length: > 0 } convenience) return convenience;

        var text = new StringBuilder();

        foreach (var item in node["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() is not ("message" or null)) continue;

            foreach (var part in item?["content"]?.AsArray() ?? [])
            {
                if (part?["type"]?.GetValue<string>() is "output_text" or "text")
                    text.Append(part["text"]?.GetValue<string>());
            }
        }

        return text.ToString();
    }

    private static AiUsage? ReadUsage(JsonNode? usage) =>
        usage is null
            ? null
            : new AiUsage(
                usage["input_tokens"]?.GetValue<int>() ?? 0,
                usage["output_tokens"]?.GetValue<int>() ?? 0);

    private async Task<AiException> FailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var status = (int)response.StatusCode;

        var message = TryReadMessage(body) ?? body;
        logger.LogError("OpenAI returned {Status}: {Body}", status, message);

        return new AiException($"OpenAI request failed ({status}). {message}")
        {
            Retryable = status >= 500 || status is 429 or 408,

            // A rejected model name is worth exactly one thing: trying the fallback
            UnknownModel = status == 404
                || (status == 400 && message.Contains("model", StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static string? TryReadMessage(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsWorthRetrying(Exception ex, CancellationToken ct) => ex switch
    {
        AiException ai => ai.Retryable,
        HttpRequestException => true,
        TaskCanceledException => !ct.IsCancellationRequested,   // a timeout, not the user leaving
        _ => false,
    };

    private void Require()
    {
        if (!_options.IsConfigured) throw new AiException(_options.ConfigurationProblem);
    }
}
