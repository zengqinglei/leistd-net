using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Leistd.Security.Users;
using Leistd.RealTime.AspNetCore.SignalR.Services;
using Leistd.RealTime.Abstractions;

namespace Leistd.RealTime.AspNetCore.SignalR.Hubs;

/// <summary>
/// 实时业务事件 Hub：客户端订阅/取消订阅资源，资源变更时收到推送。
/// </summary>
public class RealTimeHub(
    ICurrentUser currentUser,
    IRealTimeSubscriptionAuthorizer subscriptionAuthorizer,
    ILogger<RealTimeHub> logger) : Hub
{
    /// <summary>订阅资源变更。</summary>
    /// <remarks>
    /// 授权器<b>无条件</b>参与判定：没有"关掉校验"的开关。公共资源场景显式注册
    /// <c>AllowAllRealTimeSubscriptionAuthorizer</c>，把那个决定写在宿主代码里。
    /// </remarks>
    /// <param name="resourceKey">资源标识（如 "product-profile:{id}"）。</param>
    public async Task Subscribe(string resourceKey)
    {
        var userId = currentUser.Id?.ToString() ?? Context.UserIdentifier;
        var allowed = await subscriptionAuthorizer.AuthorizeAsync(
            new RealTimeSubscriptionContext(resourceKey, userId),
            Context.ConnectionAborted);

        if (!allowed)
        {
            throw new HubException("Subscription forbidden.");
        }

        // 租户来自环境上下文，由 SignalR 基座的 Hub 过滤器在本次调用前建立。
        var groupName = RealTimeGroups.Resource(resourceKey);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        logger.LogInformation("RealTimeHub Subscribe: connection={ConnectionId}, group={Group}", Context.ConnectionId, groupName);
    }

    /// <summary>取消订阅资源变更。</summary>
    public async Task Unsubscribe(string resourceKey)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            RealTimeGroups.Resource(resourceKey));
    }
}
