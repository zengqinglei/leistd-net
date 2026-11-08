using Leistd.EventBus.Tests.Contracts;

namespace Leistd.EventBus.Tests.RabbitMQ;

/// <summary>RabbitMQ 运行共用持久接收与确认契约。</summary>
public sealed class RabbitMqEventTransportContractTests : DistributedEventTransportContract
{
    /// <inheritdoc />
    protected override string TransportKind => "rabbitmq";
}
