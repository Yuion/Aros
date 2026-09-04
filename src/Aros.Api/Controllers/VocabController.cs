using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Vocab;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

public record VocabAnswerRequest(Guid Token, string? Text, int? SelectedWordId);
public record AddWordRequest(string? Characters);
public record VocabEditRequest(string? Pinyin, string? English, string[]? Tags, string? Notes);

[ApiController]
[Route("api/[controller]")]
public class VocabController(
    AppDbContext db,
    VocabService vocab,
    VocabHarvester harvester,
    CedictImporter importer) : ControllerBase
{
    [HttpGet("words")]
    public async Task<IActionResult> Words([FromQuery] bool? needsReview, CancellationToken ct)
    {
        var query = db.VocabWords.Include(w => w.Progress).AsNoTracking();
        if (needsReview is { } flag) query = query.Where(w => w.NeedsReview == flag);

        var words = await query
            .OrderBy(w => w.Characters)
            .Select(w => new
            {
                id = w.Id,
                characters = w.Characters,
                pinyin = w.Pinyin,
                english = w.English,
                tags = w.Tags,
                notes = w.Notes,
                needsReview = w.NeedsReview,
                readingAlternatives = w.ReadingAlternatives,
                correct = w.Progress.Sum(p => p.CorrectCount),
                wrong = w.Progress.Sum(p => p.WrongCount),
            })
            .ToListAsync(ct);

        return Ok(words);
    }

    /// <summary>Add a character or word directly, without going through a sentence.</summary>
    [HttpPost("words")]
    public async Task<IActionResult> Add([FromBody] AddWordRequest request, CancellationToken ct)
    {
        try
        {
            var words = await harvester.AddAsync(request.Characters, ct);

            return Ok(new
            {
                added = words.Select(word => new
                {
                    id = word.Id,
                    characters = word.Characters,
                    pinyin = word.Pinyin,
                    english = word.English,
                    needsReview = word.NeedsReview,
                }),
            });
        }
        catch (VocabException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Fix a harvested entry and clear its review flag so it enters the rotation.</summary>
    [HttpPut("words/{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] VocabEditRequest request, CancellationToken ct)
    {
        var word = await db.VocabWords.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (word is null) return NotFound();

        if (request.Pinyin is not null)
            word.Pinyin = CedictImporter.ForDisplay(CedictImporter.NormalizePinyin(request.Pinyin));
        if (request.English is not null) word.English = request.English.Trim();
        if (request.Tags is not null) word.Tags = [.. request.Tags.Select(t => t.Trim()).Where(t => t.Length > 0)];
        if (request.Notes is not null) word.Notes = request.Notes.Trim() is { Length: > 0 } n ? n : null;

        // Confirmed by hand — it can be tested from here on
        word.NeedsReview = false;
        word.ReadingAlternatives = null;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("words/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var word = await db.VocabWords.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (word is null) return NotFound();

        db.VocabWords.Remove(word);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>What each direction has left to ask, so the start button can be right before it is pressed.</summary>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability([FromQuery] string? tag, CancellationToken ct)
    {
        var directions = await vocab.AvailabilityAsync(tag, ct);

        return Ok(directions.Select(d => new
        {
            direction = d.Key,
            ready = d.Ready,
            resting = d.Resting,
            mastered = d.Mastered,
            total = d.Total,
            restingOut = d.RestingOut,
            nextDueAt = d.NextDueAt,
            nextDue = d.NextDueAt is { } due ? Aros.Api.Scheduling.Availability.Due(due) : null,
        }));
    }

    /// <param name="sweep">
    /// Default. Take every word that is not resting, once each, rather than a sample of them.
    /// </param>
    [HttpPost("session")]
    public async Task<IActionResult> Session(
        [FromQuery] int perDirection = VocabService.DefaultPerDirection,
        [FromQuery] VocabDirection? direction = null,
        [FromQuery] string? tag = null,
        [FromQuery] bool sweep = true,
        CancellationToken ct = default)
    {
        try
        {
            var session = await vocab.BuildSessionAsync(perDirection, direction, tag, sweep, ct);

            return Ok(new
            {
                questions = session.Questions.Select(q => new
                {
                    token = q.Token,
                    direction = q.Direction.ToString(),
                    prompt = q.Prompt,
                    promptLabel = q.PromptLabel,
                    answerLabel = q.AnswerLabel,
                    typed = q.Typed,
                    options = q.Options?.Select(o => new { wordId = o.WordId, characters = o.Characters }),
                }),
            });
        }
        catch (VocabException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("answer")]
    public async Task<IActionResult> Answer([FromBody] VocabAnswerRequest request, CancellationToken ct)
    {
        try
        {
            var result = await vocab.AnswerAsync(request.Token, request.Text, request.SelectedWordId, ct);

            return Ok(new
            {
                correct = result.Correct,
                expected = result.Expected,
                characters = result.Characters,
                note = result.Note,
                retry = result.Retry,
            });
        }
        catch (VocabException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Collect vocabulary from every sentence already in the TTS library.</summary>
    [HttpPost("harvest/backfill")]
    public async Task<IActionResult> Backfill(CancellationToken ct)
    {
        var sentences = await db.TtsClips.AsNoTracking().Select(c => c.Sentence).ToListAsync(ct);

        var added = 0;
        var review = 0;

        foreach (var sentence in sentences)
        {
            var result = await harvester.HarvestAsync(sentence, ct);
            added += result.Added;
            review += result.NeedsReview;
        }

        return Ok(new { sentences = sentences.Count, added, needsReview = review });
    }

    [HttpGet("dictionary/status")]
    public async Task<IActionResult> DictionaryStatus(CancellationToken ct) =>
        Ok(new { entries = await db.DictionaryEntries.CountAsync(ct) });

    /// <summary>Downloads CC-CEDICT and loads it. Minutes on a Pi, seconds on a desktop.</summary>
    [HttpPost("dictionary/import")]
    public async Task<IActionResult> DictionaryImport([FromQuery] bool force = false, CancellationToken ct = default)
    {
        var result = await importer.ImportAsync(force, ct);

        return Ok(new
        {
            entries = result.Imported,
            alreadyPresent = result.AlreadyPresent,
            source = CedictImporter.SourceUrl,
        });
    }
}
