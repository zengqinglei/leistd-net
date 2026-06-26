using Leistd.Timing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.Notifications.AspNetCore.SignalR;

/// <summary>
/// <see cref="INotificationPublisher"/> 的 SignalR 实现：先持久化（若注册了 store），再实时推送。
/// </summary>
/// <remarks>
/// 注册为 Singleton（IHubContext 是 Singleton）。INotificationStore 为 Scoped，
/// 通过 <see cref="IServiceScopeFactory"/> 按需创建 scope 访问。推送/持久化失败仅记日志，不抛出。
/// </remarks>
internal class SignalRNotificationNotifier(
    IHubContext<NotificationHub> notificationHub,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<SignalRNotificationNotifier> logger) : INotificationPublisher
{
    private const string EventName = "NotificationReceived";

    public async Task PublishToUserAsync(string userId, AppNotification notification, CancellationToken ct = default)
    {
        notification = EnsureCreationTime(notification);
        await PersistAsync(notification, userId, ct);

        try
        {
            await notificationHub.Clients.Group($"user:{userId}").SendAsync(EventName, notification, ct);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to push notification {Id} to user {UserId}", notification.Id, userId);
        }
    }

    public async Task PublishToGroupAsync(string groupName, AppNotification notification, CancellationToken ct = default)
    {
        // 群组推送不持久化（无法确定组内具体用户）
        notification = EnsureCreationTime(notification);
        try
        {
            await notificationHub.Clients.Group(groupName).SendAsync(EventName, notification, ct);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to push notification to group {GroupName}", groupName);
        }
    }

    public async Task PublishToAllAsync(AppNotification notification, CancellationToken ct = default)
    {
        notification = EnsureCreationTime(notification);
        try
        {
            await notificationHub.Clients.All.SendAsync(EventName, notification, ct);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast notification {Id}", notification.Id);
        }
    }

    private AppNotification EnsureCreationTime(AppNotification notification)
        => notification.CreationTime == default
            ? notification with { CreationTime = clock.Now }
            : notification;

    private async Task PersistAsync(AppNotification notification, string userId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetService<INotificationStore>();
            if (store != null)
            {
                await store.SaveAsync(notification, userId, ct);
            }
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Failed to persist notification {Id} for user {UserId}", notification.Id, userId);
        }
    }
}
