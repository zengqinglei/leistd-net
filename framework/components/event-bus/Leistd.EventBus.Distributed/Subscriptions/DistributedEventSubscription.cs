namespace Leistd.EventBus.Distributed.Subscriptions;

/// <summary>登记的事件类型与消费者；传输据此建立路由。</summary>
public sealed record DistributedEventSubscription
{
    /// <summary>线上事件名。</summary>
    public required string EventName { get; init; }

    /// <summary>本服务反序列化使用的事件类型。</summary>
    public required Type EventType { get; init; }

    /// <summary>处理器类型，由调用方显式注册，不扫描程序集。</summary>
    public required Type HandlerType { get; init; }

    /// <summary>稳定消费者名，参与去重；重命名处理器时应显式保持原值。</summary>
    public required string Consumer { get; init; }
}
