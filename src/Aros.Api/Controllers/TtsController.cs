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
            var (clip, cached) = await tts.GetOrCreateAsync(request.Text, ct);

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

    [HttpGet("clips")]
    public async Task<IActionResult> Clips(CancellationToken ct)
    {
        var clips = await db.TtsClips
            .Include(c => c.Stat)
            .OrderByDescending(c => c.CreatedAt)
            .AsNoTracking()
            .Select(c => new
            {
                id = c.Id,
                sentence = c.Sentence,
                voice = c.Voice,
                durationSeconds = c.DurationSeconds,
                createdAt = c.CreatedAt,
                correctCount = c.Stat != null ? c.Stat.CorrectCount : 0,
                wrongCount = c.Stat != null ? c.Stat.WrongCount : 0,
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
