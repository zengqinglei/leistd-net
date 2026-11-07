namespace Leistd.EventBus.Distributed.Management;

/// <summary>持久记录的状态。</summary>
public enum DistributedEventInboxStatus
{
    /// <summary>待处理，包括退避重试。</summary>
    Pending,
    /// <summary>已处理，允许主动重放。</summary>
    Processed,
    /// <summary>处理次数耗尽，允许重放。</summary>
    Failed
}
