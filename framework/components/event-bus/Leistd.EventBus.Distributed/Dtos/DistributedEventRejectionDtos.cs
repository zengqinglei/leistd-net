using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Leistd.Data.Paging;
using Leistd.EventBus.Distributed.Stores;

namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>按 Id 倒序的偏移分页查询；Sorting 不生效。</summary>
public record GetDistributedEventRejectionsInputDto : PageRequest
{
    /// <summary>宿主物理库选择键：host 或代表租户 Id；租户不得传。</summary>
    public string? Database { get; init; }

    /// <summary>事件名精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? EventName { get; init; }

    /// <summary>事件标识精确匹配；无法解析标识的拒收记录不匹配关键字。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>首次加载时刻上限（UTC），含等于该时刻的记录。</summary>
    public DateTime? Before { get; init; }

    /// <summary>状态精确匹配；查询串只接受 camelCase 名称。</summary>
    [EnumDataType(typeof(DistributedEventRejectionStatus), ErrorMessage = "{0} is not an allowed value.")]
    public DistributedEventRejectionStatus? Status { get; init; }

    /// <summary>拒收原因码精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Reason { get; init; }
}

/// <summary>宿主集中拒收概要；不含原始字节或消息头。</summary>
public sealed record DistributedEventRejectionOutputDto
{
    /// <summary>记录标识，JSON 为十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public required long Id { get; init; }

    /// <summary>保存原始消息的传输名。</summary>
    public required string Transport { get; init; }

    /// <summary>不透明的来源坐标，用于概要中定位无法解码的消息；不含消息头或原始字节。</summary>
    public IReadOnlyDictionary<string, string> Coordinates { get; init; } = new Dictionary<string, string>();

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

/// <summary>详情概要；没有管理权限也可读取，载荷不随之返回。</summary>
public sealed record DistributedEventRejectionDetailOutputDto
{
    /// <summary>与列表相同的概要。</summary>
    public required DistributedEventRejectionOutputDto Record { get; init; }

    /// <summary>冻结的消息头。</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>不透明的传输坐标。</summary>
    public required IReadOnlyDictionary<string, string> Coordinates { get; init; }
}

/// <summary>按条件恢复筛选；发件箱只选死信，拒收只选 Pending。</summary>
public sealed record DistributedEventRejectionFilterDto
{
    /// <summary>本次恢复的时间上限（UTC），必填，防止卷入后续新记录。</summary>
    [Required(ErrorMessage = "{0} is required.")]
    public DateTime? Before { get; init; }

    /// <summary>事件名精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? EventName { get; init; }

    /// <summary>事件标识精确匹配；无法解析标识的拒收记录不匹配关键字。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>拒收原因码精确匹配；只恢复 Pending。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Reason { get; init; }
}

/// <summary>恢复目标；Ids 与 Filter 必须二选一；逐项返回跳过原因。</summary>
public sealed record RecoverDistributedEventRejectionInputDto
{
    /// <summary>勾选的记录标识，最多 100，JSON 接受十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public IReadOnlyList<long>? Ids { get; init; }

    /// <summary>按当前条件恢复，不接受分页参数。</summary>
    public DistributedEventRejectionFilterDto? Filter { get; init; }
}

/// <summary>仅宿主 Manage 策略可读取的拒收原始消息。</summary>
public sealed record DistributedEventRawMessageOutputDto
{
    /// <summary>原始字节，JSON 为 Base64 字符串；允许零长度消息。</summary>
    public required byte[] RawBody { get; init; }
}
