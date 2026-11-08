namespace Leistd.EventBus.Distributed.Transport;

/// <summary>发件箱交给传输的事件信封。</summary>
public sealed record OutgoingDistributedEvent : DistributedEventMessage;
