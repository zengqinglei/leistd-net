namespace Leistd.Notifications;

/// <summary>
/// 通知发布器 —— 业务层唯一入口，封装推送能力，不感知底层传输（SignalR 等）。
/// </summary>
public interface INotificationPublisher
{
    /// <summary>推送通知给指定用户。</summary>
    Task PublishToUserAsync(string userId, AppNotification notification, CancellationToken ct = default);

    /// <summary>推送通知给指定用户组。</summary>
    Task PublishToGroupAsync(string groupName, AppNotification notification, CancellationToken ct = default);

    /// <summary>推送通知给所有在线用户。</summary>
    Task PublishToAllAsync(AppNotification notification, CancellationToken ct = default);
}
