using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aros.Api.Vocab;

public record ImportResult(int Imported, bool AlreadyPresent);

/// <summary>
/// Loads CC-CEDICT into the database. Lines look like:
///   漢字 汉字 [han4 zi4] /Chinese character/CL:個|个[ge4]/
/// Its pinyin is already tone-numbered and space-separated, which is the app's canonical form;
/// only ü differs, written u: by CC-CEDICT and v here.
/// </summary>
public partial class CedictImporter(AppDbContext db, IHttpClientFactory http, ILogger<CedictImporter> logger)
{
    public const string SourceUrl =
        "https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz";

    [GeneratedRegex(@"^(\S+)\s+(\S+)\s+\[([^\]]*)\]\s+/(.*)/\s*$")]
    private static partial Regex LineFormat();

    public async Task<ImportResult> ImportAsync(bool force, CancellationToken ct)
    {
        var existing = await db.DictionaryEntries.CountAsync(ct);
        if (existing > 0 && !force) return new ImportResult(existing, AlreadyPresent: true);

        logger.LogInformation("Downloading CC-CEDICT from {Url}", SourceUrl);

        var client = http.CreateClient(nameof(CedictImporter));
        await using var compressed = await client.GetStreamAsync(SourceUrl, ct);
        await using var plain = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(plain, Encoding.UTF8);

        var entries = new List<DictionaryEntry>();
        var skipped = 0;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;

            var match = LineFormat().Match(line);
            if (!match.Success) { skipped++; continue; }

            entries.Add(new DictionaryEntry
            {
                Traditional = match.Groups[1].Value,
                Simplified = match.Groups[2].Value,
                Pinyin = NormalizePinyin(match.Groups[3].Value),
                English = match.Groups[4].Value,
            });
        }

        logger.LogInformation("Parsed {Count} entries ({Skipped} unparseable)", entries.Count, skipped);

        if (existing > 0) await db.Database.ExecuteSqlRawAsync("DELETE FROM \"DictionaryEntries\"", ct);
        await BulkInsertAsync(entries, ct);

        return new ImportResult(entries.Count, AlreadyPresent: false);
    }

    /// <summary>
    /// CC-CEDICT's own conventions, brought to the app's: ü is written `u:` there and `v` here.
    /// Tone numbers and spacing already match, and the neutral tone is already 5.
    /// Case is deliberately preserved — CC-CEDICT capitalises the pinyin of proper nouns
    /// (`Shui3` the surname against `shui3` water), which is the only signal available for
    /// telling a headline sense from a name. Comparison is case-insensitive anyway.
    /// </summary>
    public static string NormalizePinyin(string raw) =>
        raw.Replace("u:", "v").Replace("U:", "V").Trim();

    /// <summary>The form stored on a vocabulary entry and shown to the user.</summary>
    public static string ForDisplay(string pinyin) =>
        pinyin.ToLower(CultureInfo.InvariantCulture);

    /// <summary>
    /// 125k rows through the change tracker is minutes of work and hundreds of MB; COPY does it
    /// in a couple of seconds, which matters more on a Pi than in dev.
    /// </summary>
    private async Task BulkInsertAsync(List<DictionaryEntry> entries, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY \"DictionaryEntries\" (\"Simplified\", \"Traditional\", \"Pinyin\", \"English\") FROM STDIN (FORMAT BINARY)",
            ct);

        foreach (var entry in entries)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(entry.Simplified, ct);
            await writer.WriteAsync(entry.Traditional, ct);
            await writer.WriteAsync(entry.Pinyin, ct);
            await writer.WriteAsync(entry.English, ct);
        }

        await writer.CompleteAsync(ct);
    }
}
