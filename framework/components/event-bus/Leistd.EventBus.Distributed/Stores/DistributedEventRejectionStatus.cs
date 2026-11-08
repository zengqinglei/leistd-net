namespace Leistd.EventBus.Distributed.Stores;

/// <summary>持久记录的状态。</summary>
public enum DistributedEventRejectionStatus
{
    /// <summary>尚未恢复或丢弃。</summary>
    Pending,
    /// <summary>重新解码成功。</summary>
    Redecoded,
    /// <summary>已主动丢弃。</summary>
    Discarded,
    /// <summary>已转交给匹配传输的接收宿主，等待重新解码。</summary>
    RedecodePending
}
