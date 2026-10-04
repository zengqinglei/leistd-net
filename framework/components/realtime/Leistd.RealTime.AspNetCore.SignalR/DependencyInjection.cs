using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.AspNetCore.SignalR;
using Leistd.RealTime.AspNetCore.SignalR.Hubs;
using Leistd.RealTime.AspNetCore.SignalR.Publishing;
using Leistd.RealTime.Publishing;
using Leistd.RealTime.Subscriptions;

namespace Leistd.RealTime.AspNetCore.SignalR;

/// <summary>
/// Leistd 实时（SignalR）依赖注入与端点映射配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>业务事件 Hub 的默认路径。</summary>
    public const string DefaultRealTimeHubPath = "/hubs/realtime";

    /// <summary>
    /// 注册 SignalR 实时基础设施：业务事件推送器。
    /// </summary>
    /// <remarks>
    /// 内部调用 <c>AddSignalR()</c>。通知组件（Leistd.Notifications.AspNetCore.SignalR）
    /// 可在此基础上叠加自己的 Hub 与发布器。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRealTimeSignalR();
    ///
    /// app.MapRealTimeHub();   // 默认 /hubs/realtime
    /// </code>
    /// </example>
    public static IServiceCollection AddRealTimeSignalR(this IServiceCollection services)
    {
        services.AddRealTime();
        // 走 SignalR 基座而不是裸 AddSignalR：Hub 方法调用不经中间件，
        // 主体/租户/链路标识与 UserIdentifier 解析全靠基座。
        services.AddSignalRAmbientContext();

        // 幂等：宿主同时装通知与实时时两个入口都会走到这里，重复注册会让
        // IBusinessEventPublisher 出现两条，按 IEnumerable 解析时同一事件推两遍。
        services.TryAddSingleton<IBusinessEventPublisher, SignalRBusinessEventPublisher>();

        return services;
    }

    /// <summary>映射实时业务事件 Hub 端点（需登录）。</summary>
    /// <remarks>
    /// 返回官方的 <see cref="HubEndpointConventionBuilder"/>：宿主可继续链式追加授权策略、CORS 等端点约定。
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapRealTimeHub().RequireAuthorization("Realtime");
    /// </code>
    /// </example>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <param name="pattern">Hub 路径，默认 <see cref="DefaultRealTimeHubPath"/>。</param>
    public static HubEndpointConventionBuilder MapRealTimeHub(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultRealTimeHubPath)
    {
        // 授权器缺失即失败关闭：Subscribe 无条件走授权器，没有它连接会在首次订阅时
        // 因解析不到依赖而失败——那太晚且信息含糊。这里明确指出该注册什么。
        // 只问"注册了没有"、不在根容器里解析它：授权器通常要按请求判权限（依赖作用域服务），
        // 从根容器解析会被作用域校验拒绝，或者在不校验时捕获一份不该跨请求共享的实例。
        var probe = endpoints.ServiceProvider.GetService<IServiceProviderIsService>();
        // 框架的 Microsoft DI 容器提供探针；没有探针时沿用组件的组合检查约定跳过，
        // 不为未声明支持的容器从根作用域构造授权器。
        if (probe is not null && !probe.IsService(typeof(IRealTimeSubscriptionAuthorizer)))
        {
            throw new InvalidOperationException(
                $"No {nameof(IRealTimeSubscriptionAuthorizer)} is registered. Every Subscribe call goes " +
                "through it, so the host must decide who may subscribe to which resource. Register your " +
                "own authorizer, or call AddAllowAllRealTimeSubscriptions() to state explicitly that any " +
                "authenticated client may subscribe to any resource key.");
        }

        var hub = endpoints.MapHub<RealTimeHub>(pattern);
        hub.RequireAuthorization();
        return hub;
    }
}
