using System.Text.Json;

namespace Aros.Api.Sync;

public record SyncRequest(List<SyncAction> Actions);
public record SyncAction(int Id, string Type, JsonElement Payload, string Timestamp);
