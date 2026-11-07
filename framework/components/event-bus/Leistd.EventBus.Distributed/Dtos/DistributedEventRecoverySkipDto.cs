using System.Text.Json.Serialization;
namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>单条或勾选恢复未成功的记录。</summary>
public sealed record DistributedEventRecoverySkipDto
{
    /// <summary>记录标识，JSON 为十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public required long Id { get; init; }

    /// <summary>稳定原因：not-found、invalid-state 或 redecode-failed。</summary>
    public required string ReasonCode { get; init; }
}
