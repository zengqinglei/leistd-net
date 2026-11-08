using System.Text.Json.Serialization;
using Leistd.EventBus.Distributed.Errors;

namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>恢复结果；按条件分批提交后返回累计条数。</summary>
public sealed record DistributedEventRecoveryOutputDto
{
    /// <summary>实际改动的记录数，含已登记的重新解码待办。</summary>
    public required long AffectedCount { get; init; }

    /// <summary>已转交重新解码的条数，属于 AffectedCount 的子集，默认 0。</summary>
    public long DeferredCount { get; init; }

    /// <summary>未改动的标识和原因；按条件不返回不存在项。</summary>
    public required IReadOnlyList<DistributedEventRecoverySkipDto> Skipped { get; init; }
}

/// <summary>单条或勾选恢复未成功的记录。</summary>
public sealed record DistributedEventRecoverySkipDto
{
    /// <summary>记录标识，JSON 为十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public required long Id { get; init; }

    /// <summary>跳过原因，取值见 <see cref="DistributedEventRecoverySkipReasons"/>。</summary>
    public required string ReasonCode { get; init; }
}
