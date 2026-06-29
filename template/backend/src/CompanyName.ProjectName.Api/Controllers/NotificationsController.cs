using Leistd.Notifications;
using Leistd.Security.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 通知中心：当前用户的通知列表 / 未读数 / 标记已读。
/// </summary>
[Authorize]
[Route("api/v1/notifications")]
public class NotificationsController(
    INotificationStore notificationStore,
    ICurrentUser currentUser) : BaseController
{
    /// <summary>获取当前用户通知列表（按创建时间倒序）。</summary>
    [HttpGet]
    public async Task<IReadOnlyList<NotificationOutputDto>> GetListAsync(
        [FromQuery] int maxCount = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            return [];

        return await notificationStore.GetByUserAsync(userId, maxCount, cancellationToken);
    }

    /// <summary>获取当前用户未读通知数量。</summary>
    [HttpGet("unread-count")]
    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            return 0;

        return await notificationStore.GetUnreadCountAsync(userId, cancellationToken);
    }

    /// <summary>标记单条通知为已读。</summary>
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsReadAsync(string id, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            return Forbid();

        await notificationStore.MarkAsReadAsync(id, userId, cancellationToken);
        return NoContent();
    }

    /// <summary>标记当前用户所有通知为已读。</summary>
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            return Forbid();

        await notificationStore.MarkAllAsReadAsync(userId, cancellationToken);
        return NoContent();
    }
}
