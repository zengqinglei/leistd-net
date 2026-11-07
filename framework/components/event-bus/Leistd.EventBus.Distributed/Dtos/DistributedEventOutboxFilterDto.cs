using System.ComponentModel.DataAnnotations;
namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>按条件恢复筛选；发件箱只选死信，拒收只选 Pending。</summary>
public sealed record DistributedEventOutboxFilterDto
{
    /// <summary>本次恢复的时间上限（UTC），必填，防止卷入后续新记录。</summary>
    [Required(ErrorMessage = "{0} is required.")]
    public DateTime? Before { get; init; }

    /// <summary>事件名精确匹配。</summary>
    public string? EventName { get; init; }

    /// <summary>事件标识或排序键精确匹配。</summary>
    public string? Keyword { get; init; }

    /// <summary>最近错误码精确匹配。</summary>
    public string? ErrorCode { get; init; }
}
