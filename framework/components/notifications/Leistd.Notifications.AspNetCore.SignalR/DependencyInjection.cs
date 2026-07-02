using Leistd.RealTime.AspNetCore.SignalR;
using Leistd.RealTime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Claims;

namespace Leistd.Notifications.AspNetCore.SignalR;

/// <summary>
/// 通知（SignalR 传输）依赖注入与端点映射配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>通知 Hub 路径。</summary>
    public const string DefaultNotificationHubPath = "/hubs/notifications";

    /// <summary>
    /// 注册基于 SignalR 的通知发布器。
    /// </summary>
    /// <remarks>
    /// 内部只注册通知传输所需的 SignalR 能力，不注册实时业务 Hub、在线状态或业务事件发布器。
    /// 通知持久化请另行调用 <c>AddNotificationsEfCore&lt;TDbContext&gt;()</c>。
    /// </remarks>
    public static IServiceCollection AddNotificationsSignalR(
        this IServiceCollection services,
        Action<RealTimeOptions>? configure = null)
    {
        services.AddNotifications();
        services.AddNotificationSignalRTransport(configure);
        services.AddSingleton<INotificationSender, SignalRNotificationSender>();
        return services;
    }

    /// <summary>
    /// 注册通知 SignalR 传输所需的最小基础设施。
    /// </summary>
    public static IServiceCollection AddNotificationSignalRTransport(
        this IServiceCollection services,
        Action<RealTimeOptions>? configure = null)
    {
        var options = new RealTimeOptions();
        configure?.Invoke(options);
        services.Configure<RealTimeOptions>(opt =>
        {
            opt.RealTimeHubPath = options.RealTimeHubPath;
            opt.KeepAliveInterval = options.KeepAliveInterval;
            opt.ClientTimeoutInterval = options.ClientTimeoutInterval;
            opt.EnableDetailedErrors = options.EnableDetailedErrors;
            opt.UserIdClaimTypes = options.UserIdClaimTypes.Count > 0
                ? options.UserIdClaimTypes
                : ["sub", ClaimTypes.NameIdentifier];
            opt.EnableRedisBackplane = options.EnableRedisBackplane;
            opt.RedisConnectionString = options.RedisConnectionString;
            opt.RequireSubscriptionAuthorization = options.RequireSubscriptionAuthorization;
        });

        services.AddSignalR(opt =>
        {
            opt.EnableDetailedErrors = options.EnableDetailedErrors;
            opt.KeepAliveInterval = options.KeepAliveInterval;
            opt.ClientTimeoutInterval = options.ClientTimeoutInterval;
        });

        services.TryAddSingleton<IUserIdProvider, ClaimsSignalRUserIdProvider>();
        return services;
    }

    /// <summary>
    /// 映射通知 Hub 端点（需登录）。
    /// </summary>
    /// <remarks>
    /// 只负责通知自身的 Hub。实时业务事件 Hub 由实时组件的 <c>MapRealTimeHub()</c> 显式映射，
    /// 避免通知组件越权代映射、以及与调用方重复映射 /hubs/realtime。
    /// </remarks>
    public static IEndpointRouteBuilder MapNotificationHub(
        this IEndpointRouteBuilder endpoints,
        string notificationHubPath = DefaultNotificationHubPath)
    {
        endpoints.MapHub<NotificationHub>(notificationHubPath).RequireAuthorization();
        return endpoints;
    }
}
