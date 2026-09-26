using Aros.Api.Data.Entities;
using Aros.Api.Writing;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

/// <summary>
/// One character, as it was written. The stroke grading runs in the browser, against the same
/// median data the stroke-order box uses, so what arrives here is the verdict rather than the ink.
/// </summary>
public record WritingAttemptRequest(
    int WordId,
    string Character,
    string Mode,
    int Mistakes,
    bool Backwards,
    int Strokes,
    int DurationMs);

[ApiController]
[Route("api/[controller]")]
public class WritingController(WritingService writing) : ControllerBase
{
    /// <summary>Every word worth writing, with its record in each mode.</summary>
    [HttpGet("words")]
    public async Task<IActionResult> Words(CancellationToken ct)
    {
        var words = await writing.WordsAsync(ct);

        return Ok(words.Select(w => new
        {
            id = w.Id,
            characters = w.Characters,
            pinyin = w.Pinyin,
            english = w.English,
            copying = w.Copying,
            memory = w.Memory,
        }));
    }

    [HttpPost("attempts")]
    public async Task<IActionResult> Record([FromBody] WritingAttemptRequest request, CancellationToken ct)
    {
        var attempt = new WritingAttempt
        {
            WordId = request.WordId,
            Character = request.Character,
            Mode = request.Mode,
            Mistakes = Math.Max(0, request.Mistakes),
            Backwards = request.Backwards,
            Strokes = Math.Max(0, request.Strokes),
            DurationMs = Math.Max(0, request.DurationMs),
        };

        return await writing.RecordAsync(attempt, ct)
            ? NoContent()
            : BadRequest(new { message = "Not a word and a single character in a known mode." });
    }
}
