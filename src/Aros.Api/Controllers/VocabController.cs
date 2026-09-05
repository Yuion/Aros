using Aros.Api.Data;
using Aros.Api.Data.Entities;
using Aros.Api.Tts;
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
public class VocabController(AppDbContext db, VocabService vocab, VocabImporter dump, TtsService tts) : ControllerBase
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
                hasAudio = w.AudioLocation != "",
                audioUrl = $"/api/vocab/words/{w.Id}/audio",
                correct = w.Progress.Sum(p => p.CorrectCount),
                wrong = w.Progress.Sum(p => p.WrongCount),
            })
            .ToListAsync(ct);

        return Ok(words);
    }

    /// <summary>
    /// Speaks a word, buying one synthesis the first time. Words are spoken on request rather
    /// than on import: a pasted list of forty is forty paid calls, and that should be a decision
    /// rather than a side effect of pasting.
    /// </summary>
    [HttpPost("words/{id:int}/audio")]
    public async Task<IActionResult> Speak(int id, CancellationToken ct)
    {
        var word = await db.VocabWords.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (word is null) return NotFound();

        try
        {
            if (!tts.FileExists(word.AudioLocation))
            {
                word.AudioLocation = await tts.SpeakFragmentAsync(word.Characters, ct);
                await db.SaveChangesAsync(ct);
            }

            return Ok(new { id = word.Id, audioUrl = $"/api/vocab/words/{word.Id}/audio" });
        }
        catch (TtsException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Speaks every word that has none. One paid call per word, so the count is worth knowing
    /// first — the caller is expected to have said how many before asking.
    /// </summary>
    [HttpPost("words/audio/missing")]
    public async Task<IActionResult> SpeakMissing(CancellationToken ct)
    {
        var words = await db.VocabWords.OrderBy(w => w.Characters).ToListAsync(ct);

        var spoken = 0;
        var alreadyHad = 0;
        var failures = new List<object>();

        // One at a time: each is a paid call, and a partial run that reports what it managed
        // beats a parallel one that half-fails halfway through your credit.
        foreach (var word in words)
        {
            if (tts.FileExists(word.AudioLocation))
            {
                alreadyHad++;
                continue;
            }

            try
            {
                word.AudioLocation = await tts.SpeakFragmentAsync(word.Characters, ct);
                await db.SaveChangesAsync(ct);
                spoken++;
            }
            catch (Exception ex) when (ex is TtsException or HttpRequestException)
            {
                failures.Add(new { characters = word.Characters, message = ex.Message });
            }
        }

        return Ok(new { spoken, alreadyHad, failures });
    }

    [HttpGet("words/{id:int}/audio")]
    public async Task<IActionResult> Audio(int id, CancellationToken ct)
    {
        var word = await db.VocabWords.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (word is null) return NotFound();
        if (!tts.FileExists(word.AudioLocation))
            return NotFound(new { message = "This word has not been spoken yet." });

        return File(tts.OpenFile(word.AudioLocation), "audio/mpeg", enableRangeProcessing: true);
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
