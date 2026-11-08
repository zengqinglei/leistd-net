using Leistd.EventBus.Events;

namespace Leistd.EventBus.Distributed.Events;

/// <summary>验证发布、传输与收件箱处理链路的内置事件。</summary>
[EventName(DistributedEventTestEvent.Name)]
public sealed class DistributedEventTestEvent : DistributedEvent
{
    /// <summary>内置测试事件的稳定名称。</summary>
    public const string Name = "event-bus.test";

    /// <summary>内置测试处理器的稳定消费者名，参与收件箱去重。</summary>
    public const string ConsumerName = "Leistd.EventBus.Test";
}
