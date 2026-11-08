using Leistd.EventBus.Distributed.Stores;
using Leistd.MultiTenancy.Tenancy;

namespace Leistd.EventBus.EntityFrameworkCore.Entities;

/// <summary>每个消费者独立的收件箱，处理与业务写入共享工作单元。</summary>
public class DistributedEventInbox : IMultiTenant
{
    /// <summary>物理库内自增的记录标识。</summary>
    public long Id { get; set; }

    /// <summary>发布方的原始事件标识，恢复时不改变。</summary>
    public Guid EventId { get; set; }

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>跨服务稳定事件名。</summary>
    public string EventName { get; set; } = default!;

    /// <summary>可选排序键；同租户同事件名同键串行。</summary>
    public string? OrderingKey { get; set; }

    /// <summary>原始业务发生时间（UTC）。</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>发布或接收时冻结的 JSON 载荷。</summary>
    public string Payload { get; set; } = default!;

    /// <summary>消息头和链路元数据。</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>稳定消费者名，参与去重。</summary>
    public string Consumer { get; set; } = default!;

    /// <summary>不透明的传输坐标。</summary>
    public Dictionary<string, string> Coordinates { get; set; } = new();

    /// <summary>持久处理状态。</summary>
    public DistributedEventInboxStatus Status { get; set; }

    /// <summary>本轮处理次数，也是乐观并发令牌；每次成功和失败均递增。</summary>
    public int AttemptCount { get; set; }

    /// <summary>下一次处理时刻（UTC）。</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>最近处理失败的稳定错误码。</summary>
    public string? LastErrorCode { get; set; }

    /// <summary>最近诊断信息，最多 512 个字符。</summary>
    public string? LastError { get; set; }

    /// <summary>额外收到相同事件的次数。</summary>
    public int DuplicateCount { get; set; }

    /// <summary>手动重放轮数。</summary>
    public int ReplayCount { get; set; }

    /// <summary>首次持久接收时刻（UTC）。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>处理成功时刻（UTC）。</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>最后状态变更或恢复时刻（UTC），保留期据此计算。</summary>
    public DateTime LastModificationTime { get; set; }
}
