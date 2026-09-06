using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Leistd.AspNetCore.SignalR;
using Leistd.RealTime.Options;
using Leistd.RealTime.AspNetCore.SignalR.Hubs;
using Leistd.RealTime.AspNetCore.SignalR.Services;
using Leistd.RealTime.Abstractions;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// Leistd 实时（SignalR）依赖注入与端点映射配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 SignalR 实时基础设施：业务事件推送器。
    /// </summary>
    /// <remarks>
    /// 内部调用 <c>AddSignalR()</c>。通知组件（Leistd.Notifications.AspNetCore.SignalR）
    /// 可在此基础上叠加自己的 Hub 与发布器。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRealTimeSignalR(builder.Configuration);
    ///
    /// app.MapRealTimeHub();   // 默认 /hubs/realtime
    /// </code>
    /// </example>
    public static IServiceCollection AddRealTimeSignalR(
        this IServiceCollection services,
        Action<RealTimeOptions>? configure = null)
    {
        services.AddOptions<RealTimeOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddRealTime();
        // 走 SignalR 基座而不是裸 AddSignalR：Hub 方法调用不经中间件，
        // 主体/租户/链路标识与 UserIdentifier 解析全靠基座。
        services.AddSignalRAmbientContext();

        services.AddSingleton<IBusinessEventPublisher, SignalRBusinessEventPublisher>();

        return services;
    }

    /// <summary>映射实时业务事件 Hub 端点（需登录）。</summary>
    public static IEndpointRouteBuilder MapRealTimeHub(this IEndpointRouteBuilder endpoints)
    {
        // 授权器缺失即失败关闭：Subscribe 无条件走授权器，没有它连接会在首次订阅时
        // 因解析不到依赖而失败——那太晚且信息含糊。这里明确指出该注册什么。
        if (endpoints.ServiceProvider.GetService<IRealTimeSubscriptionAuthorizer>() is null)
        {
            throw new InvalidOperationException(
                $"No {nameof(IRealTimeSubscriptionAuthorizer)} is registered. Every Subscribe call goes " +
                "through it, so the host must decide who may subscribe to which resource. Register your " +
                "own authorizer, or call AddAllowAllRealTimeSubscriptions() to state explicitly that any " +
                "authenticated client may subscribe to any resource key.");
        }

        var options = endpoints.ServiceProvider.GetService<IOptions<RealTimeOptions>>()?.Value ?? new RealTimeOptions();
        endpoints.MapHub<RealTimeHub>(options.RealTimeHubPath).RequireAuthorization();
        return endpoints;
    }
}
