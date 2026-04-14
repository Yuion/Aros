using System.Text.Json;

namespace Aros.Api.Sync;

public interface ISyncHandler
{
    string ActionType { get; }
    Task HandleAsync(JsonElement payload, CancellationToken ct = default);
}
