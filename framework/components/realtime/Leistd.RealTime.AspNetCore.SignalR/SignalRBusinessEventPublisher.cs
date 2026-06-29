using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Leistd.RealTime;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// <see cref="IBusinessEventPublisher"/> 的 SignalR 实现：推送到 resource:{key} 组。
/// </summary>
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
        var groupName = $"resource:{resourceKey}";
        try
        {
            await realTimeHub.Clients.Group(groupName).SendAsync(eventName, @event, ct);
            logger.LogInformation("Event {EventName} pushed to group {Group}", eventName, groupName);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to push event {EventName} to group {Group}", eventName, groupName);
        }
    }
}
