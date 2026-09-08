using Leistd.Timing;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Abstractions;
using Microsoft.Extensions.Logging;

namespace Leistd.Notifications.Services;

/// <summary>
/// 默认通知发布器：负责补齐通知信息、持久化用户通知，并委托投递器完成实时推送。
/// </summary>
/// <remarks>
/// <para>存储是<b>必需</b>依赖：通知的定义就是"有历史、可补看、计入未读数"，没有历史的
/// 定向推送是另一件事。存储可选会让宿主漏装持久化时得到一个静默降级的系统——
/// 实时推送照常到达，刷新页面后铃铛空白。未注册实现时在解析发布器处直接失败。</para>
/// <para>发布器只消费<b>一个</b>权威存储：多个传输通道（SignalR、推送、邮件）说得通，
/// 多个持久化去处说不通——那只会带来「写了一半」的不一致，而没有对应的真实场景。
/// 单值注入本身拦不住重复注册（Microsoft DI 会静默取最后一条），
/// <c>AddNotificationsEfCore</c> 因此在注册处拒绝换用另一个上下文。</para>
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
        // 身份在这里定案，而不是由调用方带进来：同一份内容发给两个人是两条独立记录。
        // 曾经入参就是 NotificationOutputDto，调用方复用一个对象扇出时两条记录带同一个主键，
        // 第二次落库冲突；而客户端拿到的 ID 究竟指公告还是指"我这条"也无法解释。
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

        // 先落库再推送：推送失败只是这一次没送到，历史还在；反过来则是历史丢了。
        // 两侧用同一个对象，保证数据库里的 ID 与实时推送里的 ID 完全一致——
        // 客户端拿着推送里的 ID 去标记已读，必须能命中自己那条记录。
        await store.SaveAsync(userNotification, userId, ct);

        // 跨渠道隔离归编排层，不指望每个实现自己记得吞异常：一个宿主自定义渠道抛错
        // 不该让后面的渠道收不到——历史已经落库，这次发布在业务上已经成立。
        // 取消例外：那是调用方主动放弃这次操作，要如实向上传播，不能伪装成"某个渠道失败了"。
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
