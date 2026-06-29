using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.RealTime;
using Leistd.Security.Users;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// 实时业务事件 Hub：客户端订阅/取消订阅资源，资源变更时收到推送。
/// </summary>
public class RealTimeHub(
    ICurrentUser currentUser,
    IRealtimeSubscriptionAuthorizer subscriptionAuthorizer,
    IOptions<RealTimeOptions> options,
    ILogger<RealTimeHub> logger) : Hub
{
    /// <summary>连接建立时，将连接加入用户组 user:{userId}，并标记在线。</summary>
    public override async Task OnConnectedAsync()
    {
        var userId = currentUser.Id?.ToString() ?? Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            SignalRPresenceService.UserConnected(userId);
        }
        await base.OnConnectedAsync();
    }

    /// <summary>连接断开时，标记下线。</summary>
    public override async Task OnDisconnectedAsync(System.Exception? exception)
    {
        var userId = currentUser.Id?.ToString() ?? Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            SignalRPresenceService.UserDisconnected(userId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>订阅资源变更。</summary>
    /// <param name="resourceKey">资源标识（如 "product-profile:{id}"）。</param>
    public async Task Subscribe(string resourceKey)
    {
        if (options.Value.RequireSubscriptionAuthorization)
        {
            var userId = currentUser.Id?.ToString() ?? Context.UserIdentifier;
            var allowed = await subscriptionAuthorizer.AuthorizeAsync(
                new RealtimeSubscriptionContext(resourceKey, userId),
                Context.ConnectionAborted);

            if (!allowed)
            {
                throw new HubException("Subscription forbidden.");
            }
        }

        var groupName = $"resource:{resourceKey}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        logger.LogInformation("RealTimeHub Subscribe: connection={ConnectionId}, group={Group}", Context.ConnectionId, groupName);
    }

    /// <summary>取消订阅资源变更。</summary>
    public async Task Unsubscribe(string resourceKey)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"resource:{resourceKey}");
    }
}
