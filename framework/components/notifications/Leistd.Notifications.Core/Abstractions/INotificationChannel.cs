using Leistd.Notifications.Dtos;

namespace Leistd.Notifications.Abstractions;

/// <summary>
/// 通过指定介质投递已定案的通知，不负责持久化。
/// </summary>
/// <remarks>
/// 业务发布使用 <see cref="INotificationPublisher"/>，它先持久化，再依次调用所有渠道。
/// 渠道可累加注册；投递失败直接抛出，由发布器记录并隔离。
/// 调用方已取消时，发布器传播 <see cref="OperationCanceledException"/>；渠道自身超时按普通故障隔离。
/// </remarks>
public interface INotificationChannel
{
    /// <summary>把通知投递给指定用户。</summary>
    /// <param name="userId">收件人标识；渠道负责将其映射到投递目标。</param>
    /// <param name="notification">已由发布器定案的通知（含 <c>Id</c>、<c>CreationTime</c>）。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeliverAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default);
}
