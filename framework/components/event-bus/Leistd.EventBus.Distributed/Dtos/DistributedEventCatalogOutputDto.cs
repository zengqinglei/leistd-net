namespace Leistd.EventBus.Distributed.Dtos;

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
