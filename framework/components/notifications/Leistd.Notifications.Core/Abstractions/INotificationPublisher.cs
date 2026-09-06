using Leistd.Notifications.Dtos;

namespace Leistd.Notifications.Abstractions;

/// <summary>
/// 通知发布器 —— 业务层唯一入口，封装推送能力，不感知底层传输（SignalR 等）。
/// </summary>
/// <remarks>
/// <b>只面向人</b>：每条通知都有归属用户、写入历史、计入未读数。这是"站内通知"的定义——
/// 有收件人、有已读状态、离线也能补看。<br/>
/// 需要推给"此刻在线的连接"（无归属、无历史）时用 realtime 组件的
/// <c>IBusinessEventPublisher</c>：那是瞬态推送的职责所在，不是没有历史的通知。<br/>
/// 全员公告要进历史时是扇出——受众由业务决定，逐个调用本接口即可。
/// </remarks>
public interface INotificationPublisher
{
    /// <summary>推送通知给指定用户，并写入该用户的通知历史。</summary>
    Task PublishToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default);

}

/// <summary>
/// 通知投递器 —— 只负责传输，不负责持久化。
/// </summary>
public interface INotificationSender
{
    /// <summary>投递通知给指定用户。</summary>
    Task SendToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default);

}
