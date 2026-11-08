using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Leistd.Data.Paging;
using Leistd.EventBus.Distributed.Stores;

namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>按 Id 倒序的偏移分页查询；Sorting 不生效。</summary>
public record GetDistributedEventOutboxInputDto : PageRequest
{
    /// <summary>宿主物理库选择键：host 或代表租户 Id；租户不得传。</summary>
    public string? Database { get; init; }

    /// <summary>事件名精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? EventName { get; init; }

    /// <summary>事件标识、排序键、TraceId 或 CorrelationId 精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>首次加载时刻上限（UTC），含等于该时刻的记录。</summary>
    public DateTime? Before { get; init; }

    /// <summary>状态精确匹配；查询串只接受 camelCase 名称。</summary>
    [EnumDataType(typeof(DistributedEventOutboxStatus), ErrorMessage = "{0} is not an allowed value.")]
    public DistributedEventOutboxStatus? Status { get; init; }

    /// <summary>最近错误码精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? ErrorCode { get; init; }
}

/// <summary>发件箱概要；不含载荷或消息头。</summary>
public sealed record DistributedEventOutboxOutputDto
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

    /// <summary>累计发送尝试次数，手动重投不清零。</summary>
    public required int AttemptCount { get; init; }

    /// <summary>Pending 时的下一次尝试时刻（UTC）。</summary>
    public DateTime? NextAttemptAt { get; init; }

    /// <summary>最近失败的稳定错误码。</summary>
    public string? LastErrorCode { get; init; }

    /// <summary>最近失败的诊断文本，最多 512 个字符。</summary>
    public string? LastError { get; init; }

    /// <summary>最后状态变更时刻（UTC），保留期以此计算。</summary>
    public required DateTime LastModificationTime { get; init; }

    /// <summary>持久状态；Pending 且次数大于零时界面显示重试中。</summary>
    public required DistributedEventOutboxStatus Status { get; init; }

    /// <summary>管理员重投次数。</summary>
    public required int RedeliverCount { get; init; }

    /// <summary>写入发件箱的时刻（UTC）。</summary>
    public required DateTime CreationTime { get; init; }

    /// <summary>本轮首次发送失败的时刻（UTC）。</summary>
    public DateTime? FirstFailureAt { get; init; }

    /// <summary>成功发送时刻（UTC）。</summary>
    public DateTime? SentAt { get; init; }
}

/// <summary>详情概要；没有管理权限也可读取，载荷不随之返回。</summary>
public sealed record DistributedEventOutboxDetailOutputDto
{
    /// <summary>与列表相同的概要。</summary>
    public required DistributedEventOutboxOutputDto Record { get; init; }

    /// <summary>冻结的消息头。</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}

/// <summary>按条件恢复筛选；发件箱只选死信，拒收只选 Pending。</summary>
public sealed record DistributedEventOutboxFilterDto
{
    /// <summary>本次恢复的时间上限（UTC），必填，防止卷入后续新记录。</summary>
    [Required(ErrorMessage = "{0} is required.")]
    public DateTime? Before { get; init; }

    /// <summary>事件名精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? EventName { get; init; }

    /// <summary>事件标识、排序键、TraceId 或 CorrelationId 精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>最近错误码精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? ErrorCode { get; init; }
}

/// <summary>恢复目标；Ids 与 Filter 必须二选一；逐项返回跳过原因。</summary>
public sealed record RecoverDistributedEventOutboxInputDto
{
    /// <summary>勾选的记录标识，最多 100，JSON 接受十进制字符串。</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public IReadOnlyList<long>? Ids { get; init; }

    /// <summary>按当前条件恢复，不接受分页参数。</summary>
    public DistributedEventOutboxFilterDto? Filter { get; init; }
}

/// <summary>仅 Manage 策略可读取的发件箱或收件箱 JSON 载荷。</summary>
public sealed record DistributedEventPayloadOutputDto
{
    /// <summary>冻结的 JSON 文本。</summary>
    public required string Payload { get; init; }
}
