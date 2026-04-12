using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Aros.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private static readonly Dictionary<string, Func<JsonElement, Task>> _handlers = new();

    /// <summary>
    /// Register a handler for a sync action type.
    /// Call this from your feature controllers at startup.
    /// </summary>
    public static void RegisterHandler(string type, Func<JsonElement, Task> handler)
        => _handlers[type] = handler;

    [HttpPost]
    public async Task<IActionResult> Sync([FromBody] SyncRequest request)
    {
        var syncedIds = new List<int>();

        foreach (var action in request.Actions)
        {
            if (_handlers.TryGetValue(action.Type, out var handler))
            {
                try
                {
                    await handler(action.Payload);
                    syncedIds.Add(action.Id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[sync] Failed to handle '{action.Type}': {ex.Message}");
                }
            }
            else
            {
                // Unknown type — still ack it so the client doesn't retry forever
                Console.WriteLine($"[sync] No handler for action type '{action.Type}'");
                syncedIds.Add(action.Id);
            }
        }

        return Ok(new { syncedIds });
    }
}

public record SyncRequest(List<SyncAction> Actions);
public record SyncAction(int Id, string Type, JsonElement Payload, string Timestamp);
