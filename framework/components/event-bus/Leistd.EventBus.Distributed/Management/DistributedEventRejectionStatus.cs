namespace Leistd.EventBus.Distributed.Management;

/// <summary>持久记录的状态。</summary>
public enum DistributedEventRejectionStatus
{
    /// <summary>尚未恢复或丢弃。</summary>
    Pending,
    /// <summary>重新解码成功。</summary>
    Redecoded,
    /// <summary>已主动丢弃。</summary>
    Discarded
}
