using System.Text.Json.Nodes;
using System.Text.Json;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Events;

namespace Leistd.EventBus.EntityFrameworkCore.Receiving;

internal static class DistributedEventPayload
{
    public static string Prepare(IncomingDistributedEvent message)
    {
        var payload = JsonNode.Parse(message.Payload) as JsonObject
            ?? throw new JsonException("The distributed event payload must be an object.");
        foreach (var key in payload.Select(x => x.Key).Where(x =>
                     x.Equals("eventId", StringComparison.OrdinalIgnoreCase) || x.Equals("occurredOn", StringComparison.OrdinalIgnoreCase)).ToArray())
            payload.Remove(key);
        payload["eventId"] = message.EventId;
        payload["occurredOn"] = message.OccurredAt;
        return payload.ToJsonString(JsonSerializerOptions.Web);
    }

    public static IDistributedEvent Deserialize(string payload, Type type)
        => JsonSerializer.Deserialize(payload, type, JsonSerializerOptions.Web) as IDistributedEvent
            ?? throw new JsonException("The payload did not produce a distributed event.");
}
