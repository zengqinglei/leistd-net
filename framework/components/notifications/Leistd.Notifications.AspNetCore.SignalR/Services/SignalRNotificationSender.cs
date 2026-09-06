using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Leistd.Notifications.AspNetCore.SignalR.Hubs;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Abstractions;

namespace Leistd.Notifications.AspNetCore.SignalR.Services;

/// <summary>
/// 基于 SignalR 的通知投递器。
/// </summary>
public class SignalRNotificationSender(
    IHubContext<NotificationHub> notificationHub,
    ILogger<SignalRNotificationSender> logger) : INotificationSender
{
    private const string EventName = "NotificationReceived";

    /// <inheritdoc />
    public async Task SendToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
    {
        try
        {
            // 用 SignalR 自带的按用户寻址，不自建 user:{id} 分组：分组要求"Hub 加组时算出的键"
            // 与"调用方传进来的键"字符串相等，而前者是框架内部推导，调用方看不见也对不齐。
            // Clients.User 匹配的是 UserIdentifier，它由基座的 UserIdProvider 一处定义。
            await notificationHub.Clients.User(userId).SendAsync(EventName, notification, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push notification {Id} to user {UserId}", notification.Id, userId);
        }
    }


}
