namespace Leistd.EventBus.Distributed.Management;

/// <summary>持久记录的状态。</summary>
public enum DistributedEventOutboxStatus
{
    /// <summary>待发送，包括退避重试。</summary>
    Pending,
    /// <summary>已发送。</summary>
    Sent,
    /// <summary>死信，允许重投。</summary>
    DeadLettered
}
