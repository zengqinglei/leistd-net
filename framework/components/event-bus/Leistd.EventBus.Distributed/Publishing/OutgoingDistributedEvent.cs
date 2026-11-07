namespace Leistd.EventBus.Distributed.Publishing;

/// <summary>发件箱交给传输的事件信封。</summary>
public sealed record OutgoingDistributedEvent : DistributedEventMessage;
