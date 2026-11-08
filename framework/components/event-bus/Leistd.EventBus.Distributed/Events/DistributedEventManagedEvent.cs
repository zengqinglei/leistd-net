using Leistd.EventBus.Events;
namespace Leistd.EventBus.Distributed.Events;

/// <summary>持久管理动作按批提交后，在调用方上下文独立分发的审计事件。</summary>
public sealed class DistributedEventManagedEvent : LocalEvent
{
    /// <summary>成功执行的动作。</summary>
    public required DistributedEventManagementAction Action { get; init; }

    /// <summary>当前已提交批次实际改动条数。</summary>
    public required long AffectedCount { get; init; }

    /// <summary>宿主物理库选择键；租户为 null。</summary>
    public string? Database { get; init; }

    /// <summary>本批数据所属的目标租户；null 表示宿主记录。</summary>
    public Guid? TargetTenantId { get; init; }

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
    Discarded,
    /// <summary>已转交重新解码请求，尚未完成解码。</summary>
    RedecodeRequested
}
