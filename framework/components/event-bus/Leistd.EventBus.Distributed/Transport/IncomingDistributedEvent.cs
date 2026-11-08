namespace Leistd.EventBus.Distributed.Transport;

/// <summary>传输映射后的事件信封；元数据是还原事件实例的权威值。</summary>
public sealed record IncomingDistributedEvent : DistributedEventMessage
{
    /// <summary>传输坐标，只保存与展示，不参与业务路由。</summary>
    public required IReadOnlyDictionary<string, string> Coordinates { get; init; }
}
