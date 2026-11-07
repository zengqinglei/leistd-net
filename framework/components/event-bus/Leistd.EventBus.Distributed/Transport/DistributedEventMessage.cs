namespace Leistd.EventBus.Distributed.Transport;

/// <summary>传输信封的公共元数据与冻结载荷。</summary>
public abstract record DistributedEventMessage
{
    /// <summary>事件标识，重投与重放不变。</summary>
    public required Guid EventId { get; init; }

    /// <summary>跨服务事件名称。</summary>
    public required string EventName { get; init; }

    /// <summary>业务发生时刻（UTC）。</summary>
    public required DateTime OccurredAt { get; init; }

    /// <summary>消息所属租户；null 为宿主。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>排序键；null 不要求按键保序。</summary>
    public string? OrderingKey { get; init; }

    /// <summary>冻结的 JSON 文本；线上编码使用 UTF-8。</summary>
    public required string Payload { get; init; }

    /// <summary>消息头；链路与关联标识使用约定的键。</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}
