using System.Text.Json.Serialization;
using Leistd.EventBus.Distributed.Management;
namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>收件箱概要；不含载荷或消息头。</summary>
public sealed record DistributedEventInboxOutputDto
{
    /// <summary>记录标识，JSON 为十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public required long Id { get; init; }

    /// <summary>发布方的原始事件标识。</summary>
    public required Guid EventId { get; init; }

    /// <summary>跨服务事件名。</summary>
    public required string EventName { get; init; }

    /// <summary>记录所属租户；null 为宿主。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>排序键。</summary>
    public string? OrderingKey { get; init; }

    /// <summary>原始业务发生时刻（UTC）。</summary>
    public required DateTime OccurredAt { get; init; }

    /// <summary>当前一轮的尝试次数；收件箱重放后从零计数。</summary>
    public required int AttemptCount { get; init; }

    /// <summary>Pending 时的下一次尝试时刻（UTC）。</summary>
    public DateTime? NextAttemptAt { get; init; }

    /// <summary>最近失败的稳定错误码。</summary>
    public string? LastErrorCode { get; init; }

    /// <summary>最近失败的诊断文本，最多 512 个字符。</summary>
    public string? LastError { get; init; }

    /// <summary>最后状态变更时刻（UTC），保留期以此计算。</summary>
    public required DateTime LastModificationTime { get; init; }

    /// <summary>持久状态。</summary>
    public required DistributedEventInboxStatus Status { get; init; }

    /// <summary>稳定消费者名。</summary>
    public required string Consumer { get; init; }

    /// <summary>额外收到相同事件的次数。</summary>
    public required int DuplicateCount { get; init; }

    /// <summary>管理员重放次数。</summary>
    public required int ReplayCount { get; init; }

    /// <summary>首次持久接收时刻（UTC）。</summary>
    public required DateTime ReceivedAt { get; init; }

    /// <summary>成功处理时刻（UTC）。</summary>
    public DateTime? ProcessedAt { get; init; }
}
