namespace Leistd.EventBus.Distributed.Events;

/// <summary>恢复与丢弃的管理动作。</summary>
public enum DistributedEventManagementAction
{
    /// <summary>重投死信。</summary>
    Redelivered,
    /// <summary>重放收件箱终态记录。</summary>
    Replayed,
    /// <summary>重新解码拒收。</summary>
    Redecoded,
    /// <summary>丢弃拒收。</summary>
    Discarded
}
