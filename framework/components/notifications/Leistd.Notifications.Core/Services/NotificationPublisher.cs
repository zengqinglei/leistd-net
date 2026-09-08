using Leistd.Timing;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Abstractions;
using Microsoft.Extensions.Logging;

namespace Leistd.Notifications.Services;

/// <summary>
/// 为收件人创建并持久化通知记录，再交各渠道投递。
/// </summary>
/// <remarks>
/// 必须注册一个权威存储；可注册多个投递渠道。存储失败终止发布，渠道失败相互隔离。
/// </remarks>
public class NotificationPublisher(
    IClock clock,
    IEnumerable<INotificationChannel> channels,
    INotificationStore store,
    ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    /// <inheritdoc />
    public async Task PublishToUserAsync(string userId, NotificationInputDto notification, CancellationToken ct = default)
    {
        // 每次发布创建独立记录，同一内容扇出时不共享主键。
        var userNotification = new NotificationOutputDto
        {
            Id = Guid.CreateVersion7().ToString("N"),
            Title = notification.Title,
            Content = notification.Content,
            Type = notification.Type,
            Link = notification.Link,
            Icon = notification.Icon,
            IsRead = false,
            CreationTime = clock.Now,
            RelatedEntityId = notification.RelatedEntityId,
            RelatedEntityType = notification.RelatedEntityType,
            Metadata = notification.Metadata
        };

        // 先保存，再投递同一记录，确保客户端可用推送中的 ID 查询或标记已读。
        await store.SaveAsync(userNotification, userId, ct);

        // 隔离渠道故障，但不吞掉调用方的取消。
        foreach (var channel in channels)
        {
            try
            {
                await channel.DeliverAsync(userId, userNotification, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Notification channel {Channel} failed to deliver notification {NotificationId} to user {UserId}; remaining channels continue",
                    channel.GetType().Name,
                    userNotification.Id,
                    userId);
            }
        }
    }
}
