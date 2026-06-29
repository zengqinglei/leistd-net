namespace Leistd.Notifications;

/// <summary>
/// 通知发布器 —— 业务层唯一入口，封装推送能力，不感知底层传输（SignalR 等）。
/// </summary>
public interface INotificationPublisher
{
    /// <summary>推送通知给指定用户。</summary>
    Task PublishToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default);

    /// <summary>推送通知给指定用户组。</summary>
    Task PublishToGroupAsync(string groupName, NotificationOutputDto notification, CancellationToken ct = default);

    /// <summary>推送通知给所有在线用户。</summary>
    Task PublishToAllAsync(NotificationOutputDto notification, CancellationToken ct = default);
}

/// <summary>
/// 通知投递器 —— 只负责传输，不负责持久化。
/// </summary>
public interface INotificationSender
{
    /// <summary>投递通知给指定用户。</summary>
    Task SendToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default);

    /// <summary>投递通知给指定用户组。</summary>
    Task SendToGroupAsync(string groupName, NotificationOutputDto notification, CancellationToken ct = default);

    /// <summary>投递通知给所有在线用户。</summary>
    Task SendToAllAsync(NotificationOutputDto notification, CancellationToken ct = default);
}
