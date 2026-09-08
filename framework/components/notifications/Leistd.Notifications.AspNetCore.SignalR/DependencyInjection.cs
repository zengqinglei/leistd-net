using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.AspNetCore.SignalR;
using Leistd.Notifications.AspNetCore.SignalR.Hubs;
using Leistd.Notifications.AspNetCore.SignalR.Services;
using Leistd.Notifications.Abstractions;

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
    /// 通知持久化请另行调用通知 EF Core 包提供的 AddNotificationsEfCore 泛型方法。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddNotificationsSignalR();
    ///
    /// app.MapNotificationHub();   // 默认 /hubs/notifications，要求登录
    /// </code>
    /// </example>
    public static IServiceCollection AddNotificationsSignalR(this IServiceCollection services)
    {
        services.AddNotifications();
        // 走 SignalR 基座而不是裸 AddSignalR：Hub 方法调用不经中间件，主体/租户/链路标识与
        // UserIdentifier 解析全靠基座。基座注册是幂等的，与 realtime 组件同时装也只有一份过滤器。
        services.AddSignalRAmbientContext();
        // 按实现类型去重，不按服务类型：INotificationChannel 是累加型扩展点，
        // 发布器以 IEnumerable<T> 注入并逐一调用，宿主可以同时装邮件、WebPush 等通道。
        // 用 TryAddSingleton 会按服务类型判重——宿主已注册任一 Channel 时，
        // SignalR 这一路就再也进不来，且没有任何报错。
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INotificationChannel, SignalRNotificationChannel>());
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
