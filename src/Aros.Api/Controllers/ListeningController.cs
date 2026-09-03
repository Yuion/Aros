using Aros.Api.Data.Entities;
using Aros.Api.Listening;
using Aros.Api.Tts;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

public record AnswerRequest(Guid Token, int? SelectedClipId, string? Text);

[ApiController]
[Route("api/[controller]")]
public class ListeningController(ListeningService listening, TtsService tts) : ControllerBase
{
    [HttpPost("quiz")]
    public async Task<IActionResult> Quiz(
        [FromQuery] int questions = ListeningService.DefaultQuestionCount,
        [FromQuery] ListeningMode mode = ListeningMode.Characters,
        CancellationToken ct = default)
    {
        try
        {
            var quiz = await listening.BuildQuizAsync(questions, mode, ct);

            return Ok(new
            {
                mode = quiz.Mode.ToString(),
                typed = ListeningService.IsTyped(quiz.Mode),
                questions = quiz.Questions.Select(q => new
                {
                    token = q.Token,
                    audioUrl = $"/api/listening/audio/{q.Token}",
                    options = q.Options?.Select(o => new { clipId = o.ClipId, sentence = o.Sentence }),
                }),
            });
        }
        catch (ListeningException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Audio by question token, so the answer never appears in the quiz payload.</summary>
    [HttpGet("audio/{token:guid}")]
    public async Task<IActionResult> Audio(Guid token, CancellationToken ct)
    {
        try
        {
            var clip = await listening.GetClipForTokenAsync(token, ct);
            if (!tts.AudioExists(clip)) return NotFound(new { message = "The audio file for this clip is missing." });

            return File(tts.OpenAudio(clip), "audio/mpeg", enableRangeProcessing: true);
        }
        catch (ListeningException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("answer")]
    public async Task<IActionResult> Answer([FromBody] AnswerRequest request, CancellationToken ct)
    {
        try
        {
            var result = await listening.AnswerAsync(request.Token, request.SelectedClipId, request.Text, ct);

            return Ok(new
            {
                correct = result.Correct,
                correctClipId = result.CorrectClipId,
                correctSentence = result.CorrectSentence,
                expected = result.Expected,
                note = result.Note,
            });
        }
        catch (ListeningException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
