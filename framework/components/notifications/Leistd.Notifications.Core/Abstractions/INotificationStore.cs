using Leistd.Notifications.Dtos;

namespace Leistd.Notifications.Abstractions;

/// <summary>
/// 持久化用户通知、已读状态和未读计数。
/// </summary>
public interface INotificationStore
{
    /// <summary>保存通知。</summary>
    Task SaveAsync(NotificationOutputDto notification, string userId, CancellationToken ct = default);

    /// <summary>获取用户通知列表（按创建时间倒序）。</summary>
    Task<IReadOnlyList<NotificationOutputDto>> GetByUserAsync(string userId, int maxCount = 50, CancellationToken ct = default);

    /// <summary>标记单条通知为已读。</summary>
    Task MarkAsReadAsync(string notificationId, string userId, CancellationToken ct = default);

    /// <summary>标记用户所有通知为已读。</summary>
    Task MarkAllAsReadAsync(string userId, CancellationToken ct = default);

    /// <summary>获取用户未读通知数量。</summary>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default);
}
