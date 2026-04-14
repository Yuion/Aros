using Aros.Api.Sync;
using Microsoft.AspNetCore.Mvc;

namespace Aros.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController(IEnumerable<ISyncHandler> handlers, ILogger<SyncController> logger) : ControllerBase
{
    private readonly Dictionary<string, ISyncHandler> _handlers =
        handlers.ToDictionary(h => h.ActionType);

    [HttpPost]
    public async Task<IActionResult> Sync([FromBody] SyncRequest request, CancellationToken ct)
    {
        var syncedIds = new List<int>();

        foreach (var action in request.Actions)
        {
            if (_handlers.TryGetValue(action.Type, out var handler))
            {
                try
                {
                    await handler.HandleAsync(action.Payload, ct);
                    syncedIds.Add(action.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to handle sync action '{Type}'", action.Type);
                }
            }
            else
            {
                logger.LogWarning("No handler registered for sync action type '{Type}'", action.Type);
                syncedIds.Add(action.Id); // Ack unknown types — prevents infinite client retry
            }
        }

        return Ok(new { syncedIds });
    }
}
