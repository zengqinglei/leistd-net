using Leistd.Timing;

namespace Leistd.Notifications;

/// <summary>
/// 默认通知发布器：负责补齐通知信息、持久化用户通知，并委托投递器完成实时推送。
/// </summary>
public class NotificationPublisher(
    IClock clock,
    IEnumerable<INotificationStore> stores,
    IEnumerable<INotificationSender> senders) : INotificationPublisher
{
    public async Task PublishToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
    {
        notification = EnsureCreationTime(notification);

        foreach (var store in stores)
        {
            await store.SaveAsync(notification, userId, ct);
        }

        foreach (var sender in senders)
        {
            await sender.SendToUserAsync(userId, notification, ct);
        }
    }

    public async Task PublishToGroupAsync(string groupName, NotificationOutputDto notification, CancellationToken ct = default)
    {
        notification = EnsureCreationTime(notification);

        foreach (var sender in senders)
        {
            await sender.SendToGroupAsync(groupName, notification, ct);
        }
    }

    public async Task PublishToAllAsync(NotificationOutputDto notification, CancellationToken ct = default)
    {
        notification = EnsureCreationTime(notification);

        foreach (var sender in senders)
        {
            await sender.SendToAllAsync(notification, ct);
        }
    }

    private NotificationOutputDto EnsureCreationTime(NotificationOutputDto notification)
        => notification.CreationTime == default
            ? notification with { CreationTime = clock.Normalize(clock.Now) }
            : notification;
}
