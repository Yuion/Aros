using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Listening;
using Aros.Api.Scheduling;
using Aros.Api.Text;
using Aros.Api.Tts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

public record SpeakRequest(string? Text);

public record RetireRequest(bool Retired);

[ApiController]
[Route("api/[controller]")]
public class TtsController(AppDbContext db, TtsService tts) : ControllerBase
{
    /// <summary>Play a sentence — reuses the cached audio when we already own it, otherwise buys one synthesis.</summary>
    [HttpPost("speak")]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest request, CancellationToken ct)
    {
        try
        {
            var (clip, cached) = await tts.GetOrCreateAsync(request.Text, ct: ct);

            return Ok(new
            {
                id = clip.Id,
                sentence = clip.Sentence,
                voice = clip.Voice,
                durationSeconds = clip.DurationSeconds,
                cached,
                audioUrl = $"/api/tts/clips/{clip.Id}/audio",
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
        var rows = TableDump.Parse(request.Text);
        var normalized = rows.Select(r => (Row: r, Key: ChineseText.Normalize(r.Chinese))).ToList();

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
                sentence = r.Chinese,
                pinyin = r.Pinyin,
                english = r.English,
            }),
        });
    }

    /// <summary>
    /// Imports a pasted batch: one synthesis per sentence not already held, and a blank pinyin or
    /// translation filled in on those that are. Vocabulary is a separate paste, in the vocabulary
    /// trainer — a sentence brings its own audio and readings, not a guess at its word boundaries.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] SpeakRequest request, CancellationToken ct)
    {
        var rows = TableDump.Parse(request.Text);

        if (rows.Count == 0)
            return BadRequest(new { message = "Nothing to import — no line held a Chinese sentence." });

        var added = 0;
        var reused = 0;
        var failures = new List<object>();

        // One at a time: each new sentence is a paid call, and a partial import that reports
        // exactly what it managed beats a parallel one that half-fails.
        foreach (var row in rows)
        {
            try
            {
                var (clip, cached) = await tts.GetOrCreateAsync(row.Chinese, row.Pinyin, row.English, ct);
                if (cached) reused++; else added++;
            }
            // Deliberately everything: a timeout arrives as TaskCanceledException and would
            // otherwise take the rest of the batch down with it
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failures.Add(new { sentence = row.Chinese, message = ex.Message });
            }
        }

        return Ok(new { parsed = rows.Count, added, reused, failures });
    }

    [HttpGet("clips")]
    public async Task<IActionResult> Clips(CancellationToken ct)
    {
        var clips = await db.TtsClips
            .Include(c => c.Stats)
            .OrderByDescending(c => c.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(clips.Select(Describe));
    }

    /// <summary>
    /// Retire a sentence you know, or put it back. Nothing is deleted: the audio, the readings and
    /// every streak stay, and the trainer simply stops asking.
    /// </summary>
    [HttpPut("clips/{id:int}/retired")]
    public async Task<IActionResult> Retire(int id, [FromBody] RetireRequest request, CancellationToken ct)
    {
        var clip = await db.TtsClips.Include(c => c.Stats).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (clip is null) return NotFound();

        clip.RetiredAt = request.Retired ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);

        return Ok(Describe(clip));
    }

    /// <summary>
    /// One clip as the library shows it. The modes are reported apart: a sentence can be solid
    /// when you pick it out of four and hopeless when you have to write the English, and one
    /// merged score hides exactly the half worth practising.
    /// </summary>
    private static object Describe(TtsClip clip) => new
    {
        id = clip.Id,
        sentence = clip.Sentence,
        pinyin = clip.Pinyin,
        english = clip.English,
        voice = clip.Voice,
        durationSeconds = clip.DurationSeconds,
        createdAt = clip.CreatedAt,
        retiredAt = clip.RetiredAt,
        correct = clip.Stats.Sum(s => s.CorrectCount),
        wrong = clip.Stats.Sum(s => s.WrongCount),
        modes = Modes(clip),
        state = ItemState.Overall(clip.RetiredAt, Modes(clip).Select(m => m.state)),
        audioUrl = $"/api/tts/clips/{clip.Id}/audio",
    };

    private static List<ItemState.Part> Modes(TtsClip clip) =>
        [.. Enum.GetValues<ListeningMode>().Select(mode => Mode(clip, mode))];

    private static ItemState.Part Mode(TtsClip clip, ListeningMode mode)
    {
        var stat = ListeningService.Stat(clip, mode);

        return ItemState.Describe(
            mode.ToString(),
            clip.RetiredAt,
            Possible(clip, mode),
            RestSchedule.ForListening(stat?.WrongCount ?? 0),
            stat?.ConsecutiveCorrect ?? 0,
            stat?.CorrectCount ?? 0,
            stat?.WrongCount ?? 0,
            stat?.LastSeenAt);
    }

    /// <summary>A mode can only ask what the sentence carries: pinyin and English are optional.</summary>
    private static bool Possible(TtsClip clip, ListeningMode mode) => mode switch
    {
        ListeningMode.Pinyin => clip.Pinyin.Length > 0,
        ListeningMode.English => clip.English.Length > 0,
        _ => true,
    };

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
