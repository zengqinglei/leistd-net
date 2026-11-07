using System.Text.Json.Serialization;
using Leistd.EventBus.Distributed.Management;
namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>宿主集中拒收概要；不含原始字节或消息头。</summary>
public sealed record DistributedEventRejectionOutputDto
{
    /// <summary>记录标识，JSON 为十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public required long Id { get; init; }

    /// <summary>保存原始消息的传输名。</summary>
    public required string Transport { get; init; }

    /// <summary>稳定拒收原因码。</summary>
    public required string Reason { get; init; }

    /// <summary>拒收诊断文本，最多 512 个字符。</summary>
    public required string Error { get; init; }

    /// <summary>能解析出的事件标识。</summary>
    public Guid? EventId { get; init; }

    /// <summary>能解析出的事件名。</summary>
    public string? EventName { get; init; }

    /// <summary>持久状态。</summary>
    public required DistributedEventRejectionStatus Status { get; init; }

    /// <summary>重新解码的尝试次数。</summary>
    public required int RedecodeCount { get; init; }

    /// <summary>首次收到原始消息的时刻（UTC）。</summary>
    public required DateTime ReceivedAt { get; init; }

    /// <summary>重新解码成功或丢弃时刻（UTC）。</summary>
    public DateTime? ResolvedAt { get; init; }

    /// <summary>最后状态变更时刻（UTC）。</summary>
    public required DateTime LastModificationTime { get; init; }
}
