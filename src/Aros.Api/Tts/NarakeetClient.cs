using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace Aros.Api.Tts;

public record NarakeetAudio(byte[] Data, double? DurationSeconds);

public class TtsException(string message) : Exception(message)
{
    /// <summary>Whether trying the same call again could plausibly succeed.</summary>
    public bool Retryable { get; init; }
}

/// <summary>
/// Narakeet's short-content ("streaming") text-to-speech API: POST the script as the body,
/// get the finished audio back in one response. No SDK exists on NuGet, so this is the whole client.
/// </summary>
public class NarakeetClient(HttpClient http, IOptions<TtsOptions> options, ILogger<NarakeetClient> logger)
{
    private readonly TtsOptions _options = options.Value;

    /// <summary>Attempts per synthesis, including the first.</summary>
    private const int Attempts = 3;

    /// <summary>Doubles each time: 1s, then 2s.</summary>
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Synthesizes, retrying what is worth retrying: a dropped connection, a timeout, a 5xx, or a
    /// rate limit. A rejected key, a malformed request or an exhausted account are not retried —
    /// they will fail the same way three times and the caller waits three times as long to hear it.
    /// </summary>
    public async Task<NarakeetAudio> SynthesizeAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new TtsException("No Narakeet API key configured. Set Tts:ApiKey in appsettings.json.");

        var backoff = FirstBackoff;

        for (var attempt = 1; ; attempt++)
        {
            var last = attempt == Attempts;

            try
            {
                return await SendAsync(text, ct);
            }
            catch (TtsException ex) when (!last && ex.Retryable)
            {
                logger.LogWarning("Narakeet attempt {Attempt}/{Total} failed: {Message}. Retrying in {Delay}s.",
                    attempt, Attempts, ex.Message, backoff.TotalSeconds);
            }
            catch (Exception ex) when (!last && ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Narakeet attempt {Attempt}/{Total} did not complete: {Message}. Retrying in {Delay}s.",
                    attempt, Attempts, ex.Message, backoff.TotalSeconds);
            }

            await Task.Delay(backoff, ct);
            backoff *= 2;
        }
    }

    private async Task<NarakeetAudio> SendAsync(string text, CancellationToken ct)
    {
        // A request message cannot be sent twice, so each attempt builds its own
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"text-to-speech/mp3?voice={Uri.EscapeDataString(_options.Voice)}")
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("accept", "application/octet-stream");

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;

            logger.LogError("Narakeet returned {Status}: {Body}", status, body);
            throw new TtsException($"Narakeet request failed ({status}). {body}") { Retryable = IsTransient(status) };
        }

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        return new NarakeetAudio(data, ReadDuration(response));
    }

    /// <summary>Worth trying again: the server faltered, or asked us to slow down.</summary>
    private static bool IsTransient(int status) =>
        status >= 500 || status == 429 || status == 408;

    private static double? ReadDuration(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-duration-seconds", out var values)
        && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : null;
}
