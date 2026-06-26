namespace Leistd.Notifications;

/// <summary>
/// 通知持久化接口 —— 可选实现。
/// </summary>
public interface INotificationStore
{
    /// <summary>保存通知。</summary>
    Task SaveAsync(AppNotification notification, string userId, CancellationToken ct = default);

    /// <summary>获取用户通知列表（按创建时间倒序）。</summary>
    Task<IReadOnlyList<AppNotification>> GetByUserAsync(string userId, int maxCount = 50, CancellationToken ct = default);

    /// <summary>标记单条通知为已读。</summary>
    Task MarkAsReadAsync(string notificationId, string userId, CancellationToken ct = default);

    /// <summary>标记用户所有通知为已读。</summary>
    Task MarkAllAsReadAsync(string userId, CancellationToken ct = default);

    /// <summary>获取用户未读通知数量。</summary>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default);
}
