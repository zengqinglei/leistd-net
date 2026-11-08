using Leistd.EventBus.Events;
namespace Leistd.EventBus.Distributed.Events;

/// <summary>持久管理动作成功后提交分发；每个成功批次发布，审计不把未完成批次计入。</summary>
public sealed class DistributedEventManagedEvent : LocalEvent
{
    /// <summary>成功执行的动作。</summary>
    public required DistributedEventManagementAction Action { get; init; }

    /// <summary>当前已提交批次实际改动条数。</summary>
    public required long AffectedCount { get; init; }

    /// <summary>宿主物理库选择键；租户为 null。</summary>
    public string? Database { get; init; }

    /// <summary>勾选动作本批实际改动的记录标识；筛选动作可为空。</summary>
    public IReadOnlyList<long>? Ids { get; init; }

    /// <summary>包含时间上限的原筛选 JSON；勾选动作为空。</summary>
    public string? FilterJson { get; init; }
}

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
