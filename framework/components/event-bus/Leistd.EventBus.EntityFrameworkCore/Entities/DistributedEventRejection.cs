using Leistd.EventBus.Distributed.Stores;

namespace Leistd.EventBus.EntityFrameworkCore.Entities;

/// <summary>宿主集中拒收记录，不按租户过滤。</summary>
public class DistributedEventRejection
{
    /// <summary>宿主库内自增的拒收标识。</summary>
    public long Id { get; set; }

    /// <summary>原始传输名称。</summary>
    public string Transport { get; set; } = default!;

    /// <summary>稳定拒收原因码。</summary>
    public string Reason { get; set; } = default!;

    /// <summary>诊断信息，最多 512 个字符。</summary>
    public string Error { get; set; } = default!;

    /// <summary>可解析时记录线上事件名。</summary>
    public string? EventName { get; set; }

    /// <summary>可解析时记录事件标识。</summary>
    public Guid? EventId { get; set; }

    /// <summary>原始消息字节；允许空消息。</summary>
    public byte[] RawBody { get; set; } = [];

    /// <summary>原始消息头。</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>从有效 traceparent 提取的小写 W3C 链路标识，缺失或非法时为空。</summary>
    public string? TraceId { get; set; }

    /// <summary>消息头中的关联标识，与处理日志使用相同值。</summary>
    public string? CorrelationId { get; set; }

    /// <summary>原始传输坐标。</summary>
    public Dictionary<string, string> Coordinates { get; set; } = new();

    /// <summary>持久拒收状态。</summary>
    public DistributedEventRejectionStatus Status { get; set; }

    /// <summary>重新解码次数。</summary>
    public int RedecodeCount { get; set; }

    /// <summary>首次接收时间（UTC）。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>成功重新解码或丢弃时刻（UTC）。</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>最后状态变更或恢复时刻（UTC），保留期据此计算。</summary>
    public DateTime LastModificationTime { get; set; }
}
