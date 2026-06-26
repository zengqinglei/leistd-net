using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// Leistd 实时（SignalR）依赖注入与端点映射扩展。
/// </summary>
public static class RealTimeServiceCollectionExtensions
{
    /// <summary>
    /// 注册 SignalR 实时基础设施：UserIdProvider、在线状态、业务事件推送器。
    /// </summary>
    /// <remarks>
    /// 内部调用 <c>AddSignalR()</c>。通知组件（Leistd.Notifications.AspNetCore.SignalR）
    /// 可在此基础上叠加自己的 Hub 与发布器。
    /// </remarks>
    public static IServiceCollection AddLeistdRealTimeSignalR(
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
            opt.UserIdClaimTypes = options.UserIdClaimTypes;
            opt.EnableRedisBackplane = options.EnableRedisBackplane;
            opt.RedisConnectionString = options.RedisConnectionString;
        });

        services.AddSignalR(opt =>
        {
            opt.EnableDetailedErrors = options.EnableDetailedErrors;
            opt.KeepAliveInterval = options.KeepAliveInterval;
            opt.ClientTimeoutInterval = options.ClientTimeoutInterval;
        });

        services.AddSingleton<IUserIdProvider, ClaimUserIdProvider>();
        services.AddSingleton<IPresenceService, SignalRPresenceService>();
        services.AddSingleton<IBusinessEventPublisher, SignalRBusinessEventPublisher>();

        return services;
    }

    /// <summary>映射实时业务事件 Hub 端点（需登录）。</summary>
    public static IEndpointRouteBuilder MapLeistdRealTimeHub(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetService<IOptions<RealTimeOptions>>()?.Value ?? new RealTimeOptions();
        endpoints.MapHub<RealTimeHub>(options.RealTimeHubPath).RequireAuthorization();
        return endpoints;
    }
}
