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
}
