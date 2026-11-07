using System.Text.Json.Serialization;
namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>恢复目标；Ids 与 Filter 必须二选一；逐项返回跳过原因。</summary>
public sealed record RecoverDistributedEventOutboxInputDto
{
    /// <summary>勾选的记录标识，最多 100，JSON 接受十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public IReadOnlyList<long>? Ids { get; init; }

    /// <summary>按当前条件恢复，不接受分页参数。</summary>
    public DistributedEventOutboxFilterDto? Filter { get; init; }
}
