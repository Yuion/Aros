using Aros.Api.Grammar;
using Aros.Api.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

public record GrammarAnswerRequest(Guid Token, string? Text);

[ApiController]
[Route("api/[controller]")]
public class GrammarController(GrammarService grammar, GrammarLibrary library) : ControllerBase
{
    /// <summary>Every pattern, its record, and how many drills it has to ask with.</summary>
    [HttpGet("points")]
    public async Task<IActionResult> Points(CancellationToken ct)
    {
        var overview = await grammar.OverviewAsync(ct);

        return Ok(overview.Select(row => new
        {
            id = row.Point.Id,
            key = row.Point.Key,
            title = row.Point.Title,
            summary = row.Point.Summary,
            introducedInLesson = row.Point.IntroducedInLesson,
            drills = row.Items,
            state = row.State,
            correct = row.Progress?.CorrectCount ?? 0,
            wrong = row.Progress?.WrongCount ?? 0,
            streak = row.Progress?.ConsecutiveCorrect ?? 0,
            lastSeenAt = row.Progress?.LastSeenAt,
        }));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> Availability(CancellationToken ct)
    {
        var standing = await grammar.AvailabilityAsync(ct);

        return Ok(new
        {
            key = standing.Key,
            ready = standing.Ready,
            resting = standing.Resting,
            mastered = standing.Mastered,
            total = standing.Total,
            restingOut = standing.RestingOut,
            nextDueAt = standing.NextDueAt,
            nextDue = standing.NextDueAt is { } due ? Aros.Api.Scheduling.Availability.Due(due) : null,
        });
    }

    [HttpPost("round")]
    public async Task<IActionResult> Round(
        [FromQuery] int count = GrammarService.DefaultCount,
        [FromQuery] bool sweep = true,
        CancellationToken ct = default)
    {
        try
        {
            var round = await grammar.BuildRoundAsync(count, sweep, ct);

            return Ok(new
            {
                questions = round.Questions.Select(q => new
                {
                    token = q.Token,
                    pointId = q.PointId,
                    pattern = q.Pattern,
                    prompt = q.Prompt,
                    tiles = q.Tiles,
                }),
            });
        }
        catch (GrammarException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("answer")]
    public async Task<IActionResult> Answer([FromBody] GrammarAnswerRequest request, CancellationToken ct)
    {
        try
        {
            var result = await grammar.AnswerAsync(request.Token, request.Text, ct);

            return Ok(new
            {
                correct = result.Correct,
                pointId = result.PointId,
                pattern = result.Pattern,
                expected = result.Expected,
                note = result.Note,
            });
        }
        catch (GrammarException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Rebuilds the drill pool from the lessons. Safe to run repeatedly: an item already held for
    /// a pattern is skipped, so this only ever adds what the newest lessons brought.
    /// </summary>
    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild(CancellationToken ct)
    {
        var report = await library.RebuildAsync(ct);

        return Ok(new
        {
            points = report.Points,
            added = report.Added,
            skipped = report.Skipped,
            fromExercises = report.FromExercises,
            fromExamples = report.FromExamples,
        });
    }
}
