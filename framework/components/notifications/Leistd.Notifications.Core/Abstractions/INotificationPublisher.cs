using Leistd.Notifications.Dtos;

namespace Leistd.Notifications.Abstractions;

/// <summary>
/// 通知发布器 —— 业务层唯一入口，封装推送能力，不感知底层传输（SignalR 等）。
/// </summary>
/// <remarks>
/// 通知归属用户并持久化历史与已读状态。受众由业务决定，多人通知逐个发布。
/// 不需要历史的资源事件推送使用 realtime 组件。
/// </remarks>
public interface INotificationPublisher
{
    /// <summary>推送通知给指定用户，并写入该用户的通知历史。</summary>
    /// <remarks>
    /// 入参是<b>内容</b>（<see cref="NotificationInputDto"/>），不带身份：这条记录的 ID、
    /// 创建时刻与未读状态由发布器在收件人边界定案。同一份 <paramref name="notification"/>
    /// 发给多个用户，得到的是多条各自独立的记录。
    /// </remarks>
    Task PublishToUserAsync(string userId, NotificationInputDto notification, CancellationToken ct = default);

}
