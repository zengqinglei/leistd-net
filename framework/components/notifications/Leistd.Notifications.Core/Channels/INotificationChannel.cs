using Leistd.Notifications.Dtos;
using Leistd.Notifications.Filters;
using Leistd.Notifications.Publishing;

namespace Leistd.Notifications.Channels;

/// <summary>
/// 通过指定介质投递已定案的通知，不负责持久化。
/// </summary>
/// <remarks>
/// 业务发布使用 <see cref="INotificationPublisher"/>，它先持久化，再依次调用所有渠道；
/// 每个渠道投不投由 <see cref="INotificationDeliveryFilter"/> 决定。
/// 渠道可累加注册；投递失败直接抛出，由发布器记录并隔离。
/// 调用方已取消时，发布器传播 <see cref="OperationCanceledException"/>；渠道自身超时按普通故障隔离。
/// </remarks>
public interface INotificationChannel
{
    /// <summary>
    /// 站内渠道的名字：通知历史（未读数、通知列表）与实时推送共用它，两者同进同退。
    /// </summary>
    /// <remarks>
    /// 这是框架自己的渠道——发布器按它决定是否写入通知历史，框架的实时推送渠道以它为名。
    /// 业务渠道（邮件、短信）的名字由业务项目在各自的渠道实现上定义，不在这里登记。
    /// </remarks>
    public const string InAppName = "InApp";

    /// <summary>
    /// 渠道名，<see cref="INotificationDeliveryFilter"/> 按它决定投不投。
    /// </summary>
    /// <remarks>
    /// 站内实时推送用 <see cref="InAppName"/>：它与通知历史是同一个"站内"渠道，
    /// 收件人关掉站内时，历史不落、实时也不推。其他渠道各取一个稳定的名字。
    /// </remarks>
    string Name { get; }

    /// <summary>把通知投递给指定用户。</summary>
    /// <param name="userId">收件人标识；渠道负责将其映射到投递目标。</param>
    /// <param name="notification">已由发布器定案的通知（含 <c>Id</c>、<c>CreationTime</c>）。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeliverAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default);
}
