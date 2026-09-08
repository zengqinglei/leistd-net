using Microsoft.AspNetCore.SignalR;
using Leistd.Notifications.AspNetCore.SignalR.Hubs;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Abstractions;

namespace Leistd.Notifications.AspNetCore.SignalR.Services;

/// <summary>
/// 基于 SignalR 的通知外发渠道。
/// </summary>
/// <remarks>
/// 不注入日志：送达失败的记录由发布器统一做（见 <see cref="INotificationChannel"/>），
/// 这里再记一遍只会得到两条描述同一件事的日志。
/// </remarks>
public class SignalRNotificationChannel(
    IHubContext<NotificationHub> notificationHub) : INotificationChannel
{
    private const string EventName = "NotificationReceived";

    /// <inheritdoc />
    public async Task DeliverAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
    {
        // 按 UserIdentifier 寻址，其生成规则由 SignalR 基座的 UserIdProvider 统一提供。
        //
        // 这里不吞异常：跨渠道隔离与日志由发布器统一负责（见 INotificationChannel），
        // 各实现各吞一遍会让"取消"也被伪装成"送达失败"，而且这条保证会取决于每个实现者。
        await notificationHub.Clients.User(userId).SendAsync(EventName, notification, ct);
    }


}
