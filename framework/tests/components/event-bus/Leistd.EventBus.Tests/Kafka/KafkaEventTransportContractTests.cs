using Leistd.EventBus.Tests.Contracts;

namespace Leistd.EventBus.Tests.Kafka;

/// <summary>Kafka 运行共用持久接收与位点提交契约。</summary>
public sealed class KafkaEventTransportContractTests : DistributedEventTransportContract
{
    /// <inheritdoc />
    protected override string TransportKind => "kafka";
}
