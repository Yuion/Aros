using Aros.Api.Tones;
using Aros.Api.Tts;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

public record ToneAnswerRequest(Guid Token, int Tone, int DurationMs);

[ApiController]
[Route("api/[controller]")]
public class TonesController(ToneService tones, TtsService tts) : ControllerBase
{
    /// <summary>How big the bank is, how much of it can be played, and how the ear is doing.</summary>
    [HttpGet("standing")]
    public async Task<IActionResult> Standing(CancellationToken ct)
    {
        var standing = await tones.StandingAsync(ct);

        return Ok(new
        {
            standing.Sounds,
            standing.WithAudio,
            standing.Answered,
            standing.Accuracy,
            confusions = await tones.ConfusionsAsync(ct),
        });
    }

    /// <summary>
    /// Speaks whatever the bank is missing. Each character is one synthesis, once, ever — the
    /// file is named after the text — so this is safe to press twice.
    /// </summary>
    [HttpPost("bank")]
    public async Task<IActionResult> BuildBank(CancellationToken ct)
    {
        try
        {
            return Ok(new { spoken = await tones.SpeakMissingAsync(ct) });
        }
        catch (TtsException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("round")]
    public async Task<IActionResult> Round(
        [FromQuery] int questions = ToneService.DefaultRound, CancellationToken ct = default)
    {
        try
        {
            var round = await tones.BuildAsync(questions, ct);

            return Ok(new
            {
                questions = round.Questions.Select(q => new
                {
                    token = q.Token,
                    audioUrl = $"/api/tones/audio/{q.Token}",
                }),
            });
        }
        catch (ToneException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// The sound for one question. Served by token rather than by name so the answer is not in
    /// the URL — a file called "cai4.mp3" would settle the question before it was played.
    /// </summary>
    [HttpGet("audio/{token:guid}")]
    public IActionResult Audio(Guid token)
    {
        try
        {
            return File(tts.OpenFile(tones.LocationFor(token)), "audio/mpeg", enableRangeProcessing: true);
        }
        catch (ToneException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("answer")]
    public async Task<IActionResult> Answer([FromBody] ToneAnswerRequest request, CancellationToken ct)
    {
        if (request.Tone is < 1 or > 4) return BadRequest(new { message = "A tone is 1 to 4." });

        try
        {
            var result = await tones.AnswerAsync(request.Token, request.Tone, request.DurationMs, ct);

            return Ok(new
            {
                result.Correct,
                result.Tone,
                result.Character,
                result.Pinyin,
            });
        }
        catch (ToneException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
