using Aros.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aros.Api.Tutor;

public record BudgetState(int TokensUsedToday, int DailyTokenBudget, int RequestsThisHour, int MaxRequestsPerHour)
{
    public int TokensLeft => Math.Max(0, DailyTokenBudget - TokensUsedToday);
    public bool Exhausted => TokensUsedToday >= DailyTokenBudget;
    public bool TooFast => RequestsThisHour >= MaxRequestsPerHour;
}

/// <summary>
/// What stops the bill running away. Aros is closed, so there is nobody else to spend it — this
/// exists for the failure that does not care about that: a retry storm, a loop in a component, a
/// page left open re-sending. Counted from the usage the model itself reports, so it measures the
/// thing being billed rather than an estimate of it.
///
/// It lives in the API rather than the page, because a guard in the client is a suggestion.
/// </summary>
public class AiBudget(AppDbContext db, IOptions<AiOptions> options)
{
    private readonly AiOptions _options = options.Value;

    public async Task<BudgetState> StateAsync(CancellationToken ct)
    {
        // Local midnight, not UTC: the day that matters is the one on the wall
        var since = DateTime.SpecifyKind(DateTime.Now.Date, DateTimeKind.Local).ToUniversalTime();
        var hourAgo = DateTime.UtcNow.AddHours(-1);

        var today = await db.ChatMessages
            .Where(m => m.CreatedAt >= since)
            .Select(m => new { m.InputTokens, m.OutputTokens, m.CreatedAt, m.Role })
            .ToListAsync(ct);

        return new BudgetState(
            today.Sum(m => m.InputTokens + m.OutputTokens),
            _options.DailyTokenBudget,
            today.Count(m => m.Role == Data.Entities.ChatRole.User && m.CreatedAt >= hourAgo),
            _options.MaxRequestsPerHour);
    }

    /// <summary>Throws rather than returning false: there is no sensible way to carry on past this.</summary>
    public async Task RequireHeadroomAsync(CancellationToken ct)
    {
        var state = await StateAsync(ct);

        if (state.Exhausted)
            throw new AiException(
                $"Today's token budget is spent ({state.TokensUsedToday:N0} of {state.DailyTokenBudget:N0}). " +
                "It resets at midnight, or raise Ai:DailyTokenBudget.");

        if (state.TooFast)
            throw new AiException(
                $"{state.RequestsThisHour} requests in the last hour, which is the limit. " +
                "If that was not you, something is looping.");
    }
}
