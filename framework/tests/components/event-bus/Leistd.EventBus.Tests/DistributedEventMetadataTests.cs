using System.Text.Json;
using Leistd.EventBus.Events;
using Xunit;

namespace Leistd.EventBus.Tests;

public sealed class DistributedEventMetadataTests
{
    /// <summary>接收端反序列化必须保留发布方标识与发生时刻，避免处理器按新标识重复产生外部副作用。</summary>
    [Fact]
    public void Json_roundtrip_preserves_original_event_metadata()
    {
        var original = new CustomerActivatedEvent
        {
            EventId = Guid.Parse("00000000-0000-4000-8000-000000000001"),
            OccurredOn = new DateTime(2026, 11, 6, 1, 2, 3, DateTimeKind.Utc),
            CustomerId = Guid.Parse("00000000-0000-4000-8000-000000000002")
        };

        var json = JsonSerializer.Serialize(original, JsonSerializerOptions.Web);
        var restored = JsonSerializer.Deserialize<CustomerActivatedEvent>(json, JsonSerializerOptions.Web)!;

        Assert.Equal(original.EventId, restored.EventId);
        Assert.Equal(original.OccurredOn, restored.OccurredOn);
        Assert.Equal(original.CustomerId, restored.CustomerId);
    }

    private sealed class CustomerActivatedEvent : DistributedEvent
    {
        public Guid CustomerId { get; init; }
    }
}
