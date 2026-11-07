namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>当前查看范围的状态快照；拒收范围始终为宿主集中库。</summary>
public sealed record DistributedEventStatusOutputDto
{
    /// <summary>待发记录数。</summary>
    public required long PendingOutbox { get; init; }

    /// <summary>最旧待发记录时长（秒），无待发时为 null。</summary>
    public double? OldestPendingOutboxAgeSeconds { get; init; }

    /// <summary>死信数。</summary>
    public required long DeadLettered { get; init; }

    /// <summary>待处理数。</summary>
    public required long PendingInbox { get; init; }

    /// <summary>处理失败数。</summary>
    public required long FailedInbox { get; init; }

    /// <summary>宿主集中待处理拒收数；租户为 null。</summary>
    public long? PendingRejections { get; init; }

    /// <summary>近二十四小时成功发出数。</summary>
    public required long SentLast24Hours { get; init; }

    /// <summary>近二十四小时成功处理数。</summary>
    public required long ProcessedLast24Hours { get; init; }

    /// <summary>当前传输名；只写进程没有注册传输时为 null。</summary>
    public string? TransportName { get; init; }

    /// <summary>event-bus 标签健康检查聚合：healthy、degraded、unhealthy、unknown。</summary>
    public required string TransportStatus { get; init; }

    /// <summary>宿主是否启用测试事件。</summary>
    public required bool TestEventEnabled { get; init; }
}
