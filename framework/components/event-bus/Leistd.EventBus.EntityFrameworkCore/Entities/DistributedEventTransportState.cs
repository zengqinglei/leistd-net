using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Leistd.EventBus.EntityFrameworkCore.Entities;

/// <summary>宿主库中一个进程的最近传输健康上报，不属于任何租户。</summary>
public class DistributedEventTransportState
{
    /// <summary>并发进程间唯一的实例标识。</summary>
    public string Instance { get; set; } = default!;

    /// <summary>上报进程登记的传输名称。</summary>
    public string TransportName { get; set; } = default!;

    /// <summary>带 event-bus 标签的健康检查聚合结果。</summary>
    public HealthStatus Status { get; set; }

    /// <summary>本实例最近一次改变健康状态的时刻（UTC）。</summary>
    public DateTime StatusChangedAt { get; set; }

    /// <summary>本实例最近成功上报的时刻（UTC）。</summary>
    public DateTime ReportedAt { get; set; }
}
