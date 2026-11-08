using System.Text.Json;
using Leistd.EventBus.Distributed.Errors;

namespace Leistd.EventBus.Distributed.Transport;

// 内置传输共用的线上信封；第三方传输可直接实现自己的 DecodeAsync，不必依赖本编码。
internal static class DistributedEventCodec
{
    public static byte[] Encode(OutgoingDistributedEvent message)
        => JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web);

    public static IncomingDistributedEvent Decode(RawDistributedEvent raw)
    {
        try
        {
            var message = JsonSerializer.Deserialize<OutgoingDistributedEvent>(raw.Body.Span, JsonSerializerOptions.Web)
                ?? throw new JsonException("The event envelope is null.");
            if (message.EventId == Guid.Empty || string.IsNullOrWhiteSpace(message.EventName)
                || message.OccurredAt.Kind != DateTimeKind.Utc || message.Payload is null || message.Headers is null)
            {
                throw new JsonException("The event envelope contains invalid metadata.");
            }
            return new IncomingDistributedEvent
            {
                EventId = message.EventId,
                EventName = message.EventName,
                OccurredAt = message.OccurredAt,
                TenantId = message.TenantId,
                OrderingKey = message.OrderingKey,
                Payload = message.Payload,
                Headers = message.Headers,
                Coordinates = raw.Coordinates
            };
        }
        catch (JsonException exception)
        {
            throw new DistributedEventDecodeException(DistributedEventRejectionReasons.InvalidEnvelope,
                "The distributed event envelope is invalid.", exception);
        }
    }
}
