using Leistd.Notifications.Dtos;

namespace Leistd.Notifications.Abstractions;

/// <summary>
/// 持久化用户通知、已读状态和未读计数。
/// </summary>
public interface INotificationStore
{
    /// <summary>保存通知。</summary>
    /// <param name="notification">
    /// <b>已定案</b>的用户通知：<c>Id</c>、<c>CreationTime</c>、<c>IsRead</c> 由发布器在收件人
    /// 边界给定。存储只负责落库，<b>不生成也不替换</b>这三项——替换会让客户端手里的 ID 与库里
    /// 的对不上，标记已读永远命不中。
    /// </param>
    /// <param name="userId">收件人。</param>
    /// <param name="ct">取消令牌。</param>
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
