using Leistd.Timing;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Abstractions;

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
    IEnumerable<INotificationSender> senders,
    INotificationStore store) : INotificationPublisher
{
    /// <inheritdoc />
    public async Task PublishToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
    {
        notification = EnsureCreationTime(notification);

        // 先落库再推送：推送失败只是这一次没送到，历史还在；反过来则是历史丢了。
        await store.SaveAsync(notification, userId, ct);

        foreach (var sender in senders)
        {
            await sender.SendToUserAsync(userId, notification, ct);
        }
    }



    // 显式传入的时间同样归一化：框架的时间一律 UTC，调用方传 Local/Unspecified 时
    // 原样落库会让通知列表的排序基准与其它时间线不一致。
    private NotificationOutputDto EnsureCreationTime(NotificationOutputDto notification)
    {
        var creationTime = notification.CreationTime == default ? clock.Now : notification.CreationTime;
        return notification with { CreationTime = clock.Normalize(creationTime) };
    }
}
