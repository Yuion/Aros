using System.IO.Compression;
using System.Text;

namespace Aros.Api.Strokes;

/// <summary>
/// The stroke order of every character the dataset knows, held in memory.
///
/// Unpacked it is about 31 MB of JSON, which is more than a character lookup is worth paying
/// for on disk and far more than is worth paying for per request, so it ships as one gzip pack
/// and is expanded once, on the first character anybody asks for. A singleton: the second copy
/// of it would be 31 MB spent to answer the same question.
/// </summary>
public sealed class StrokeLibrary(IWebHostEnvironment environment, ILogger<StrokeLibrary> log)
{
    private readonly Lazy<Dictionary<char, string>> characters =
        new(() => Load(environment.ContentRootPath, log), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The strokes of one character as JSON, ready to hand to the page, or null for anything the
    /// dataset does not cover — punctuation, digits, a Latin letter, a rare character.
    /// </summary>
    public string? Find(char character) => characters.Value.GetValueOrDefault(character);

    public int Count => characters.Value.Count;

    private static Dictionary<char, string> Load(string contentRoot, ILogger log)
    {
        var path = Path.Combine(contentRoot, "Strokes", "strokes.pack.gz");
        var found = new Dictionary<char, string>(10_000);

        if (!File.Exists(path))
        {
            // Worth saying out loud: the page degrades to "no stroke order for this character"
            // for every character at once, which looks like a data gap rather than a missing file.
            log.LogError("Stroke pack missing at {Path} — no character will have stroke order", path);
            return found;
        }

        using var file = File.OpenRead(path);
        using var unpacked = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(unpacked, Encoding.UTF8);

        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t');
            if (tab != 1) continue;             // one character, then the tab, or the line is junk

            found[line[0]] = line[(tab + 1)..];
        }

        log.LogInformation("Stroke order loaded for {Count} characters", found.Count);
        return found;
    }
}
