using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.EventBus.Distributed.Management;
namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>按 Id 倒序的偏移分页查询；Sorting 不生效。</summary>
public record GetDistributedEventRejectionsInputDto : PageRequest
{
    /// <summary>宿主物理库选择键：host 或代表租户 Id；租户不得传。</summary>
    public string? Database { get; init; }

    /// <summary>事件名精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? EventName { get; init; }

    /// <summary>事件标识或排序键精确匹配。</summary>
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>首次加载时刻上限（UTC），含等于该时刻的记录。</summary>
    public DateTime? Before { get; init; }

    /// <summary>状态精确匹配；查询串只接受 camelCase 名称。</summary>
    [EnumDataType(typeof(DistributedEventRejectionStatus), ErrorMessage = "{0} is not an allowed value.")]
    public DistributedEventRejectionStatus? Status { get; init; }

    /// <summary>拒收原因码精确匹配。</summary>
    public string? Reason { get; init; }
}
