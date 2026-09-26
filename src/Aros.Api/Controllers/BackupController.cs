using Aros.Api.Backup;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

/// <summary>
/// Where the snapshots go and what unlocks them. The two secrets may be left blank to keep the
/// ones already stored, because the page is never sent them and so cannot send them back.
/// </summary>
public record BackupCredentialsRequest(
    string Repository, string KeyId, string? Passphrase, string? ApplicationKey);

[ApiController]
[Route("api/[controller]")]
public class BackupController(BackupService backup) : ControllerBase
{
    /// <summary>Whether it is set up, what is stored, and what is here waiting to be stored.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var snapshots = await backup.SnapshotsAsync(ct);

        return Ok(new
        {
            settings = backup.Describe(),
            local = backup.Local(),
            busy = backup.Busy,
            snapshots = snapshots.Select(s => new
            {
                id = s.Id,
                takenAt = s.TakenAt,
                host = s.Host,
                bytes = s.Bytes,
            }),
        });
    }

    [HttpPut("credentials")]
    public IActionResult Credentials([FromBody] BackupCredentialsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Repository))
            return BadRequest(new { message = "The repository is required." });

        backup.SaveCredentials(request.Repository, request.KeyId, request.Passphrase, request.ApplicationKey);
        return NoContent();
    }

    /// <summary>Takes a snapshot now. The same script the nightly task runs.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var result = await backup.BackupAsync(ct);
        return Ok(new { ok = result.Ok, output = result.Output });
    }

    /// <summary>Downloads a tenth of the stored packs and checks them against their hashes.</summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        var result = await backup.VerifyAsync(ct);
        return Ok(new { ok = result.Ok, output = result.Output });
    }

    /// <summary>
    /// Fetches a snapshot to look at, changing nothing.
    ///
    /// Putting one back is not offered here, and that is a decision rather than an omission. A
    /// restore stops the API and drops the database - run from inside the API, it would kill the
    /// process halfway through its own work, because a Windows service takes its children down
    /// with it. It belongs at a command line that outlives the thing being replaced, so the page
    /// hands over the exact command instead.
    /// </summary>
    [HttpPost("fetch/{snapshot}")]
    public async Task<IActionResult> Fetch(string snapshot, CancellationToken ct)
    {
        var result = await backup.FetchAsync(snapshot, ct);
        return Ok(new { ok = result.Ok, output = result.Output });
    }
}
