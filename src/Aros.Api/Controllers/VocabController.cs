using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Vocab;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Controllers;

public record VocabAnswerRequest(Guid Token, string? Text, int? SelectedWordId);
/// <summary>A pasted table of words. The field is the whole paste, not one word.</summary>
public record VocabDumpRequest(string? Text);
public record VocabEditRequest(string? Pinyin, string? English, string[]? Tags, string? Notes);

[ApiController]
[Route("api/[controller]")]
public class VocabController(AppDbContext db, VocabService vocab, VocabImporter dump) : ControllerBase
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

    /// <summary>What a pasted table would do, without writing anything.</summary>
    [HttpPost("import/preview")]
    public async Task<IActionResult> ImportPreview([FromBody] VocabDumpRequest request, CancellationToken ct) =>
        Ok(Describe(await dump.PreviewAsync(request.Text, ct)));

    /// <summary>
    /// Import a pasted table of words. Matching is on characters and pinyin, so a second reading of
    /// the same character is reported rather than silently replacing the first.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] VocabDumpRequest request, CancellationToken ct) =>
        Ok(Describe(await dump.ImportAsync(request.Text, ct)));

    private static object Describe(VocabImportResult result) => new
    {
        parsed = result.Parsed,
        added = result.Added,
        updated = result.Updated,
        unchanged = result.Unchanged,
        skipped = result.Skipped,
        conflicts = result.Conflicts.Select(c => new
        {
            characters = c.Characters,
            pinyin = c.Pinyin,
            heldPinyin = c.HeldPinyin,
        }),
    };

    /// <summary>Fix a harvested entry and clear its review flag so it enters the rotation.</summary>
    [HttpPut("words/{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] VocabEditRequest request, CancellationToken ct)
    {
        var word = await db.VocabWords.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (word is null) return NotFound();

        if (request.Pinyin is not null)
            word.Pinyin = Pinyin.ForDisplay(request.Pinyin);
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
}
