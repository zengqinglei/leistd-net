using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Leistd.RealTime.AspNetCore.SignalR.Hubs;
using Leistd.RealTime.Abstractions;

namespace Leistd.RealTime.AspNetCore.SignalR.Services;

// IBusinessEventPublisher 的 SignalR 实现：推送到 resource:{key} 组。
internal class SignalRBusinessEventPublisher(
    IHubContext<RealTimeHub> realTimeHub,
    ILogger<SignalRBusinessEventPublisher> logger) : IBusinessEventPublisher
{
    public async Task PublishToResourceAsync<TEvent>(
        string resourceKey,
        string eventName,
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : class
    {
        // 与订阅侧共用同一生成处：分开写会导致"推送成功但没人收到"。
        var groupName = RealTimeGroups.Resource(resourceKey);
        try
        {
            await realTimeHub.Clients.Group(groupName).SendAsync(eventName, @event, ct);
            logger.LogInformation("Event {EventName} pushed to group {Group}", eventName, groupName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push event {EventName} to group {Group}", eventName, groupName);
        }
    }
}
