using CompanyName.ProjectName.Infrastructure.Notifications;
using Leistd.Exception.Core;
using Leistd.Notifications;
using Leistd.Security.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 通知中心：当前用户的通知列表 / 未读数 / 标记已读 / 清空全部。
/// </summary>
[Authorize]
[Route("api/v1/notifications")]
public class NotificationsController(
    INotificationStore notificationStore,
    INotificationCleanupService notificationCleanup,
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
    public async Task MarkAsReadAsync(string id, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            throw new ForbiddenException("The current identity cannot operate on user notifications.")
#if (IncludeLocalization)
                .WithLocalization("Notification:IdentityCannotOperate")
#endif
                ;

        await notificationStore.MarkAsReadAsync(id, userId, cancellationToken);
    }

    /// <summary>标记当前用户所有通知为已读。</summary>
    [HttpPut("read-all")]
    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            throw new ForbiddenException("The current identity cannot operate on user notifications.")
#if (IncludeLocalization)
                .WithLocalization("Notification:IdentityCannotOperate")
#endif
                ;

        await notificationStore.MarkAllAsReadAsync(userId, cancellationToken);
    }

    /// <summary>清空当前用户的全部通知（持久删除）。</summary>
    [HttpDelete]
    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            throw new ForbiddenException("The current identity cannot operate on user notifications.")
#if (IncludeLocalization)
                .WithLocalization("Notification:IdentityCannotOperate")
#endif
                ;

        await notificationCleanup.ClearAllAsync(userId, cancellationToken);
    }

    /// <summary>删除当前用户的单条通知（持久删除）。</summary>
    [HttpDelete("{id}")]
    public async Task ClearAsync(string id, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString();
        if (string.IsNullOrEmpty(userId))
            throw new ForbiddenException("The current identity cannot operate on user notifications.")
#if (IncludeLocalization)
                .WithLocalization("Notification:IdentityCannotOperate")
#endif
                ;

        await notificationCleanup.ClearOneAsync(userId, id, cancellationToken);
    }
}
