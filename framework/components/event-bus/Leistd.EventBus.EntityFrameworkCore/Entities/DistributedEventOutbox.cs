using Leistd.EventBus.Distributed.Stores;
using Leistd.MultiTenancy.Tenancy;

namespace Leistd.EventBus.EntityFrameworkCore.Entities;

/// <summary>发布时写入的发件箱，与业务写入共享工作单元。</summary>
public class DistributedEventOutbox : IMultiTenant
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

    /// <summary>从有效 traceparent 提取的小写 W3C 链路标识，缺失或非法时为空。</summary>
    public string? TraceId { get; set; }

    /// <summary>消息头中的关联标识，与处理日志使用相同值。</summary>
    public string? CorrelationId { get; set; }

    /// <summary>持久发送状态。</summary>
    public DistributedEventOutboxStatus Status { get; set; }

    /// <summary>累计发送尝试次数，手动重投不清零。</summary>
    public int AttemptCount { get; set; }

    /// <summary>下一次发送时刻（UTC）。</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>本轮首次失败时刻（UTC），重投时清空。</summary>
    public DateTime? FirstFailureAt { get; set; }

    /// <summary>最近发送失败的稳定错误码。</summary>
    public string? LastErrorCode { get; set; }

    /// <summary>最近诊断信息，最多 512 个字符。</summary>
    public string? LastError { get; set; }

    /// <summary>手动重投次数。</summary>
    public int RedeliverCount { get; set; }

    /// <summary>持久写入时刻（UTC）。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>确认发送成功时刻（UTC）。</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>最后状态变更或恢复时刻（UTC），保留期据此计算。</summary>
    public DateTime LastModificationTime { get; set; }
}
