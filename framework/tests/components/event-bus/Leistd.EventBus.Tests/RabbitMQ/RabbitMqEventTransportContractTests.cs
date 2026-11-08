using Leistd.EventBus.Tests.Contracts;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.Tests.TestDoubles;
using Xunit;

namespace Leistd.EventBus.Tests.RabbitMQ;

/// <summary>RabbitMQ 运行共用持久接收与确认契约。</summary>
public sealed class RabbitMqEventTransportContractTests : DistributedEventTransportContract
{
    /// <inheritdoc />
    protected override string TransportKind => "rabbitmq";

    /// <summary>尚无订阅队列的事件仍按发布／订阅语义完成确认，不进入死信。</summary>
    [SkippableFact]
    public async Task Publishing_an_event_without_a_bound_queue_is_successful()
    {
        await using var fixture = await CreateAsync();
        var message = BrokerContractFixture.Message() with { EventName = "invoice.issued" };
        var result = await fixture.Transport.SendAsync(message, default);
        Assert.Equal(DistributedEventSendStatus.Succeeded, result.Status);
        Assert.Empty(fixture.State.Persisted);
    }
}
