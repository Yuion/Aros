using Aros.Api.Daily;
using Aros.Api.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

/// <summary>What was missed, as the client collected it from each answer.</summary>
public record DailyMissRequest(string? Kind, int Id, string? Mode, string? Direction);

public record DailyDrillRequest(List<DailyMissRequest>? Misses);

/// <summary>Endless practice: how many to hand over, and what has just been asked.</summary>
public record EndlessRequest(int? Count, List<DailyMissRequest>? Recent);

[ApiController]
[Route("api/[controller]")]
public class DailyController(DailyService daily) : ControllerBase
{
    /// <summary>
    /// What the day looks like before it is started: how much is due in each kind of question,
    /// how well each has been going, and what share of the session each would get. Also whether
    /// the day's work is done, which is what opens endless practice.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var tracks = await daily.TracksAsync(ct);
        var shares = DailyPlanner.Allocate(tracks, SessionBudget.Daily);

        return Ok(new
        {
            planned = shares.Sum(s => s.Count),
            due = tracks.Sum(t => t.Ready),
            unmastered = tracks.Sum(t => t.Unmastered),
            done = shares.Count == 0,
            budget = SessionBudget.Daily,
            tracks = tracks.Select(t => new
            {
                key = t.Key,
                label = t.Label,
                ready = t.Ready,
                unmastered = t.Unmastered,
                accuracy = t.Accuracy,
                weakness = t.Weakness,
                planned = shares.FirstOrDefault(s => s.Track.Key == t.Key)?.Count ?? 0,
            }),
        });
    }

    /// <summary>The day's work, mixed. Every card is answered through its own trainer's endpoint.</summary>
    [HttpPost("session")]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        try
        {
            return Ok(Describe(await daily.BuildAsync(ct)));
        }
        catch (DailyException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Practice past the day's work, a batch at a time. Refused while anything is still due:
    /// the point of it is what you do *after* the day is done.
    /// </summary>
    [HttpPost("endless")]
    public async Task<IActionResult> Endless([FromBody] EndlessRequest? request, CancellationToken ct)
    {
        var tracks = await daily.TracksAsync(ct);

        if (DailyPlanner.Allocate(tracks, SessionBudget.Daily).Count > 0)
            return BadRequest(new { message = "Today's work is not done yet. Finish the daily session first." });

        try
        {
            var session = await daily.BuildEndlessAsync(
                Misses(request?.Recent),
                request?.Count ?? DailyService.EndlessBatch,
                ct);

            return Ok(Describe(session));
        }
        catch (DailyException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Everything missed in the session just finished, asked again.</summary>
    [HttpPost("drill")]
    public async Task<IActionResult> Drill([FromBody] DailyDrillRequest request, CancellationToken ct)
    {
        var misses = Misses(request?.Misses);
        if (misses.Count == 0) return BadRequest(new { message = "Nothing to drill." });

        try
        {
            return Ok(Describe(await daily.BuildDrillAsync(misses, ct)));
        }
        catch (DailyException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static List<DailyMiss> Misses(List<DailyMissRequest>? rows) =>
    [
        .. (rows ?? [])
            .Where(r => r.Kind is "listening" or "vocab" or "grammar")
            .Select(r => new DailyMiss(r.Kind!, r.Id, r.Mode, r.Direction))
    ];

    private static object Describe(DailySession session) => new
    {
        cards = session.Cards.Select(c => new
        {
            kind = c.Kind,
            track = c.Track,
            label = c.Label,
            token = c.Token,
            typed = c.Typed,
            prompt = c.Prompt,
            promptLabel = c.PromptLabel,
            answerLabel = c.AnswerLabel,
            pattern = c.Pattern,
            tiles = c.Tiles,
            audioUrl = c.AudioUrl,
            hints = c.Hints?.Select(h => new { character = h.Character, alternatives = h.Alternatives }),
            mode = c.Mode,
            direction = c.Direction,
        }),
        shares = session.Shares.Select(s => new { key = s.Track.Key, label = s.Track.Label, count = s.Count }),
    };
}
