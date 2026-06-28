using Leistd.RealTime;
using Leistd.RealTime.AspNetCore.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Notifications.AspNetCore.SignalR;

/// <summary>
/// 通知（SignalR 传输）依赖注入与端点映射配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>通知 Hub 路径。</summary>
    public const string DefaultNotificationHubPath = "/hubs/notifications";

    /// <summary>
    /// 注册基于 SignalR 的通知发布器，并复用 Leistd 实时基础设施（在线状态、UserId 解析、业务事件）。
    /// </summary>
    /// <remarks>
    /// 内部调用实时 SignalR 基础设施（含 AddSignalR）。
    /// 通知持久化请另行调用 <c>AddNotificationsEfcore&lt;TDbContext&gt;()</c>。
    /// </remarks>
    public static IServiceCollection AddNotificationsSignalR(
        this IServiceCollection services,
        Action<RealTimeOptions>? configure = null)
    {
        services.AddNotifications();
        services.AddRealTimeSignalR(configure);
        services.AddSingleton<INotificationSender, SignalRNotificationSender>();
        return services;
    }

    /// <summary>映射通知 Hub 与实时业务事件 Hub 端点（均需登录）。</summary>
    public static IEndpointRouteBuilder MapNotificationsHubs(
        this IEndpointRouteBuilder endpoints,
        string notificationHubPath = DefaultNotificationHubPath)
    {
        endpoints.MapHub<NotificationHub>(notificationHubPath).RequireAuthorization();
        endpoints.MapRealTimeHub();
        return endpoints;
    }
}
