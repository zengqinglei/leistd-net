using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Leistd.Notifications.AspNetCore.SignalR;

/// <summary>
/// 通知 Hub —— 推送用户铃铛通知。连接时自动加入 user:{userId} 组。
/// </summary>
public class NotificationHub(ILogger<NotificationHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            logger.LogInformation("NotificationHub connected: connection={ConnectionId}, userId={UserId}", Context.ConnectionId, userId);
        }
        else
        {
            logger.LogWarning("NotificationHub connected but UserIdentifier is null: connection={ConnectionId}", Context.ConnectionId);
        }
        await base.OnConnectedAsync();
    }
}
