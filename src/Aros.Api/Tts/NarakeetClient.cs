using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace Aros.Api.Tts;

public record NarakeetAudio(byte[] Data, double? DurationSeconds);

public class TtsException(string message) : Exception(message);

/// <summary>
/// Narakeet's short-content ("streaming") text-to-speech API: POST the script as the body,
/// get the finished audio back in one response. No SDK exists on NuGet, so this is the whole client.
/// </summary>
public class NarakeetClient(HttpClient http, IOptions<TtsOptions> options, ILogger<NarakeetClient> logger)
{
    private readonly TtsOptions _options = options.Value;

    public async Task<NarakeetAudio> SynthesizeAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new TtsException("No Narakeet API key configured. Set Tts:ApiKey in appsettings.json.");

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
            logger.LogError("Narakeet returned {Status}: {Body}", (int)response.StatusCode, body);
            throw new TtsException($"Narakeet request failed ({(int)response.StatusCode}). {body}");
        }

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        return new NarakeetAudio(data, ReadDuration(response));
    }

    private static double? ReadDuration(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-duration-seconds", out var values)
        && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : null;
}
