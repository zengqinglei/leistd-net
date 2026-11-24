using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;

namespace Leistd.Notifications.Dtos;

/// <summary>当前用户通知列表的查询入参：按创建时间倒序分页，<see cref="PageRequest.Sorting"/> 不生效。</summary>
public record GetNotificationsInputDto : PageRequest
{
    /// <summary>通知列表的默认返回条数。</summary>
    public new const int DefaultLimit = 50;

    /// <summary>通知列表允许的最大返回条数。</summary>
    public new const int MaximumLimit = 100;

    /// <summary>返回条数，取值 1–<see cref="MaximumLimit"/>，默认 <see cref="DefaultLimit"/>。</summary>
    [Range(1, MaximumLimit, ErrorMessage = "{0} must be between {1} and {2}.")]
    public override int Limit { get; init; } = DefaultLimit;

    /// <summary>只取未读通知。</summary>
    public bool UnreadOnly { get; init; }
}
