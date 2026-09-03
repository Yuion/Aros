using Aros.Api.Data;
using Aros.Api.Tts;
using Aros.Api.Vocab;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

public record SpeakRequest(string? Text);

[ApiController]
[Route("api/[controller]")]
public class TtsController(AppDbContext db, TtsService tts, VocabHarvester harvester) : ControllerBase
{
    /// <summary>Play a sentence — reuses the cached audio when we already own it, otherwise buys one synthesis.</summary>
    [HttpPost("speak")]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest request, CancellationToken ct)
    {
        try
        {
            var (clip, cached) = await tts.GetOrCreateAsync(request.Text, ct: ct);

            // Every sentence entered here grows the vocabulary pool
            var harvest = await harvester.HarvestAsync(clip.Sentence, ct);

            return Ok(new
            {
                id = clip.Id,
                sentence = clip.Sentence,
                voice = clip.Voice,
                durationSeconds = clip.DurationSeconds,
                cached,
                audioUrl = $"/api/tts/clips/{clip.Id}/audio",
                newWords = harvest.Words,
                newWordsNeedingReview = harvest.NeedsReview,
            });
        }
        catch (TtsException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// What a pasted batch would do, without spending anything. Every sentence not already held
    /// costs one Narakeet synthesis, so the count is worth seeing before the import runs.
    /// </summary>
    [HttpPost("import/preview")]
    public async Task<IActionResult> ImportPreview([FromBody] SpeakRequest request, CancellationToken ct)
    {
        var rows = SentenceDump.Parse(request.Text);
        var normalized = rows.Select(r => (Row: r, Key: ChineseText.Normalize(r.Sentence))).ToList();

        var keys = normalized.Select(n => n.Key).ToList();
        var held = await db.TtsClips
            .Where(c => keys.Contains(c.Sentence))
            .Select(c => new { c.Sentence, c.Pinyin, c.English })
            .ToDictionaryAsync(c => c.Sentence, ct);

        return Ok(new
        {
            parsed = rows.Count,
            newSentences = normalized.Count(n => !held.ContainsKey(n.Key)),
            fills = normalized.Count(n =>
                held.TryGetValue(n.Key, out var c) &&
                ((c.Pinyin.Length == 0 && n.Row.Pinyin.Length > 0) ||
                 (c.English.Length == 0 && n.Row.English.Length > 0))),
            unchanged = normalized.Count(n =>
                held.TryGetValue(n.Key, out var c) &&
                (c.Pinyin.Length > 0 || n.Row.Pinyin.Length == 0) &&
                (c.English.Length > 0 || n.Row.English.Length == 0)),
            rows = rows.Take(50).Select(r => new
            {
                sentence = r.Sentence,
                pinyin = r.Pinyin,
                english = r.English,
            }),
        });
    }

    /// <summary>
    /// Imports a pasted batch: one synthesis per sentence not already held, a blank pinyin or
    /// translation filled in on those that are, and vocabulary harvested from all of them.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] SpeakRequest request, CancellationToken ct)
    {
        var rows = SentenceDump.Parse(request.Text);

        if (rows.Count == 0)
            return BadRequest(new { message = "Nothing to import — no line held a Chinese sentence." });

        var added = 0;
        var reused = 0;
        var newWords = 0;
        var failures = new List<object>();

        // One at a time: each new sentence is a paid call, and a partial import that reports
        // exactly what it managed beats a parallel one that half-fails.
        foreach (var row in rows)
        {
            try
            {
                var (clip, cached) = await tts.GetOrCreateAsync(row.Sentence, row.Pinyin, row.English, ct);
                if (cached) reused++; else added++;

                newWords += (await harvester.HarvestAsync(clip.Sentence, ct)).Added;
            }
            catch (Exception ex) when (ex is TtsException or HttpRequestException)
            {
                failures.Add(new { sentence = row.Sentence, message = ex.Message });
            }
        }

        return Ok(new { parsed = rows.Count, added, reused, newWords, failures });
    }

    [HttpGet("clips")]
    public async Task<IActionResult> Clips(CancellationToken ct)
    {
        var clips = await db.TtsClips
            .Include(c => c.Stats)
            .OrderByDescending(c => c.CreatedAt)
            .AsNoTracking()
            .Select(c => new
            {
                id = c.Id,
                sentence = c.Sentence,
                pinyin = c.Pinyin,
                english = c.English,
                voice = c.Voice,
                durationSeconds = c.DurationSeconds,
                createdAt = c.CreatedAt,
                correctCount = c.Stats.Sum(s => s.CorrectCount),
                wrongCount = c.Stats.Sum(s => s.WrongCount),
                audioUrl = $"/api/tts/clips/{c.Id}/audio",
            })
            .ToListAsync(ct);

        return Ok(clips);
    }

    [HttpGet("clips/{id:int}/audio")]
    public async Task<IActionResult> Audio(int id, CancellationToken ct)
    {
        var clip = await db.TtsClips.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (clip is null) return NotFound();
        if (!tts.AudioExists(clip)) return NotFound(new { message = "The audio file for this clip is missing." });

        return File(tts.OpenAudio(clip), "audio/mpeg", enableRangeProcessing: true);
    }

    [HttpDelete("clips/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var clip = await db.TtsClips.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (clip is null) return NotFound();

        tts.DeleteAudio(clip);
        db.TtsClips.Remove(clip);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
