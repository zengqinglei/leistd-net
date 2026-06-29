using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Leistd.Notifications.AspNetCore.SignalR;

/// <summary>
/// 基于 SignalR 的通知投递器。
/// </summary>
public class SignalRNotificationSender(
    IHubContext<NotificationHub> notificationHub,
    ILogger<SignalRNotificationSender> logger) : INotificationSender
{
    private const string EventName = "NotificationReceived";

    public async Task SendToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
    {
        try
        {
            await notificationHub.Clients.Group($"user:{userId}").SendAsync(EventName, notification, ct);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to push notification {Id} to user {UserId}", notification.Id, userId);
        }
    }

    public async Task SendToGroupAsync(string groupName, NotificationOutputDto notification, CancellationToken ct = default)
    {
        try
        {
            await notificationHub.Clients.Group(groupName).SendAsync(EventName, notification, ct);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to push notification {Id} to group {GroupName}", notification.Id, groupName);
        }
    }

    public async Task SendToAllAsync(NotificationOutputDto notification, CancellationToken ct = default)
    {
        try
        {
            await notificationHub.Clients.All.SendAsync(EventName, notification, ct);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast notification {Id}", notification.Id);
        }
    }
}
