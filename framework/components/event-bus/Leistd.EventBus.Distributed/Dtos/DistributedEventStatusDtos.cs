

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

/// <summary>事件目录：订阅与保留期内发件箱、收件箱事件名的并集。</summary>
public sealed record DistributedEventCatalogOutputDto
{
    /// <summary>跨服务事件名。</summary>
    public required string EventName { get; init; }

    /// <summary>是否在发件箱出现过。</summary>
    public required bool Outgoing { get; init; }

    /// <summary>是否已订阅或在收件箱出现过。</summary>
    public required bool Incoming { get; init; }

    /// <summary>当前登记的消费者名。</summary>
    public required IReadOnlyList<string> Consumers { get; init; }

    /// <summary>最近成功发出时刻（UTC）。</summary>
    public DateTime? LastSentAt { get; init; }

    /// <summary>最近接收时刻（UTC）。</summary>
    public DateTime? LastReceivedAt { get; init; }

    /// <summary>最近成功处理时刻（UTC）。</summary>
    public DateTime? LastProcessedAt { get; init; }

    /// <summary>待发数。</summary>
    public required long PendingOutbox { get; init; }

    /// <summary>待处理数。</summary>
    public required long PendingInbox { get; init; }

    /// <summary>死信数。</summary>
    public required long DeadLettered { get; init; }

    /// <summary>处理失败数。</summary>
    public required long FailedInbox { get; init; }

    /// <summary>最近失败记录的稳定错误码。</summary>
    public string? LastErrorCode { get; init; }
}

/// <summary>宿主可选择的物理库；不暴露连接串或指纹。</summary>
public sealed record DistributedEventDatabaseOutputDto
{
    /// <summary>host 或代表租户的 Guid 字符串。</summary>
    public required string Key { get; init; }

    /// <summary>是否宿主库。</summary>
    public required bool IsHost { get; init; }

    /// <summary>用于进入该库的代表租户。</summary>
    public Guid? RepresentativeTenantId { get; init; }

    /// <summary>目录提供的租户清单；宿主项为空，不代表无租户。</summary>
    public required IReadOnlyList<Guid> TenantIds { get; init; }
}

/// <summary>测试发布结果；按原事件标识查询发件箱与收件箱。</summary>
public sealed record DistributedEventTestOutputDto
{
    /// <summary>原事件标识。</summary>
    public required Guid EventId { get; init; }
}
