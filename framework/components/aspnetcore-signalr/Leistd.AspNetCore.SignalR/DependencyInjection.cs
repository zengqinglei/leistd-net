using Microsoft.AspNetCore.Builder;
using Leistd.AspNetCore.SignalR.Middlewares;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.AspNetCore.SignalR.Filters;
using Leistd.AspNetCore.SignalR.Options;
using Leistd.AspNetCore.SignalR.Services;
using Leistd.Security;

namespace Leistd.AspNetCore.SignalR;

/// <summary>
/// SignalR 基座的注册入口：连接主体的解析、Hub 调用的环境上下文与有效性复检。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 SignalR、<see cref="AmbientContextHubFilter"/> 与 <see cref="ClaimsSignalRUserIdProvider"/>。
    /// </summary>
    /// <remarks>
    /// 幂等注册全局过滤器与非 HTTP 环境上下文，不覆盖宿主的 HubOptions。
    /// HTTP 主体读取另由 <c>AddSecurity()</c> 注册，与本方法的调用顺序无关。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">连接主体解析与复检选项。</param>
    public static IServiceCollection AddSignalRAmbientContext(
        this IServiceCollection services,
        Action<HubIdentityOptions>? configure = null)
    {
        services.AddOptions<HubIdentityOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddAmbientContext();
        services.AddSignalR();
        UseClaimsUserIdProvider(services);

        // 多个上层组件可重复注册基座，用标记服务避免重复挂载上下文过滤器。
        if (services.Any(d => d.ServiceType == typeof(HubAmbientContextMarker)))
        {
            return services;
        }

        services.AddSingleton<HubAmbientContextMarker>();
        services.AddSingleton<AmbientContextHubFilter>();
        services.Configure<HubOptions>(options => options.AddFilter<AmbientContextHubFilter>());

        return services;
    }

    // 只顶掉 SignalR 自带的默认实现，不碰宿主的。
    //
    // AddSignalR 已注册默认 UserIdProvider；仅替换框架默认项，保留宿主自定义实现。
    //
    // 不能直接 Replace：它只移除首项并追加，可能改变宿主自定义实现的优先级。
    //
    // 因此只看"当前会胜出的那一条"：是默认实现才换掉，其余情形一律不动。
    private static void UseClaimsUserIdProvider(IServiceCollection services)
    {
        // 只看非 keyed 注册：SignalR 按非 keyed 解析 IUserIdProvider，而 keyed 描述符的
        // ImplementationType 恒为 null，混进来会被误判成"宿主实现"而提前返回，默认实现又静默留下。
        var winner = services.LastOrDefault(
            d => !d.IsKeyedService && d.ServiceType == typeof(IUserIdProvider));
        if (winner is not null && winner.ImplementationType != typeof(DefaultUserIdProvider))
        {
            return;
        }

        if (winner is not null)
        {
            services.Remove(winner);
        }

        services.AddSingleton<IUserIdProvider, ClaimsSignalRUserIdProvider>();
    }

    // 独立标记区分"本方法已注册过"与宿主自行添加的同类过滤器。
    private sealed class HubAmbientContextMarker;

    /// <summary>
    /// 在 Hub 端点上把查询串里的 <c>access_token</c> 转成 Bearer 请求头，供 JWT 等基于请求头的认证方案读取。
    /// </summary>
    /// <remarks>
    /// <para>浏览器的 WebSocket 与 SSE 连接不能带自定义头，SignalR 客户端只能把令牌放进查询串。
    /// 只作用于 Hub 端点（按端点元数据识别，与 Hub 映射的路径无关），并把令牌从查询串移除，
    /// 其它端点仍然拒绝查询串令牌——否则令牌会出现在访问日志与 Referer 里。</para>
    /// <para>必须在路由之后、认证之前调用；已带 <c>Authorization</c> 头的请求原样放行。Cookie 认证的宿主不需要它。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.UseRouting();
    /// app.UseHubAccessToken();
    /// app.UseAuthentication();
    /// </code>
    /// </example>
    /// <param name="app">应用管道。</param>
    public static IApplicationBuilder UseHubAccessToken(this IApplicationBuilder app)
        => app.UseMiddleware<HubAccessTokenMiddleware>();
}
