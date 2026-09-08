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
        // 用 SignalR 自带的按用户寻址，不自建 user:{id} 分组：分组要求"Hub 加组时算出的键"
        // 与"调用方传进来的键"字符串相等，而前者是框架内部推导，调用方看不见也对不齐。
        // Clients.User 匹配的是 UserIdentifier，它由基座的 UserIdProvider 一处定义。
        //
        // 这里不吞异常：跨渠道隔离与日志由发布器统一负责（见 INotificationChannel），
        // 各实现各吞一遍会让"取消"也被伪装成"送达失败"，而且这条保证会取决于每个实现者。
        await notificationHub.Clients.User(userId).SendAsync(EventName, notification, ct);
    }


}
