using System.Security.Cryptography;
using System.Text;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aros.Api.Tts;

public record TtsResult(TtsClip Clip, bool Cached);

public class TtsService(
    AppDbContext db,
    NarakeetClient narakeet,
    IOptions<TtsOptions> options,
    ILogger<TtsService> logger)
{
    private readonly TtsOptions _options = options.Value;

    /// <summary>
    /// Returns the clip for a sentence, synthesizing it only if we have never paid for it before.
    /// </summary>
    public async Task<TtsResult> GetOrCreateAsync(string? rawText, CancellationToken ct)
    {
        var sentence = ChineseText.Normalize(rawText);

        if (sentence.Length == 0)
            throw new TtsException("No text provided.");
        if (sentence.Length > _options.MaxCharacters)
            throw new TtsException($"Text is {sentence.Length} characters; the limit is {_options.MaxCharacters}.");

        var existing = await db.TtsClips.FirstOrDefaultAsync(c => c.Sentence == sentence, ct);

        if (existing is not null && File.Exists(ResolvePath(existing.Location)))
            return new TtsResult(existing, Cached: true);

        if (existing is not null)
            logger.LogWarning("Audio file for clip {Id} is missing at {Location}; re-synthesizing.",
                existing.Id, existing.Location);

        var audio = await narakeet.SynthesizeAsync(sentence, ct);
        var location = FileNameFor(sentence);

        Directory.CreateDirectory(_options.MediaPath);
        await File.WriteAllBytesAsync(ResolvePath(location), audio.Data, ct);

        if (existing is not null)
        {
            existing.Location = location;
            existing.Voice = _options.Voice;
            existing.DurationSeconds = audio.DurationSeconds;
            await db.SaveChangesAsync(ct);
            return new TtsResult(existing, Cached: false);
        }

        var clip = new TtsClip
        {
            Sentence = sentence,
            Location = location,
            Voice = _options.Voice,
            DurationSeconds = audio.DurationSeconds,
        };

        db.TtsClips.Add(clip);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two requests for the same new sentence may have overlapped; the unique index catches it.
            db.Entry(clip).State = EntityState.Detached;

            var raced = await FindBySentenceAsync(sentence, ct);
            if (raced is null) throw;

            return new TtsResult(raced, Cached: true);
        }

        return new TtsResult(clip, Cached: false);
    }

    public Stream OpenAudio(TtsClip clip) =>
        File.OpenRead(ResolvePath(clip.Location));

    public bool AudioExists(TtsClip clip) =>
        File.Exists(ResolvePath(clip.Location));

    public void DeleteAudio(TtsClip clip)
    {
        var path = ResolvePath(clip.Location);
        if (File.Exists(path)) File.Delete(path);
    }

    private Task<TtsClip?> FindBySentenceAsync(string sentence, CancellationToken ct) =>
        db.TtsClips.AsNoTracking().FirstOrDefaultAsync(c => c.Sentence == sentence, ct);

    private string ResolvePath(string location) =>
        Path.Combine(_options.MediaPath, Path.GetFileName(location));

    /// <summary>Content-addressed name: same sentence always lands on the same file, no illegal characters.</summary>
    private static string FileNameFor(string sentence)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sentence));
        return $"{Convert.ToHexString(hash).ToLowerInvariant()}.mp3";
    }
}
