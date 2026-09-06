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
    /// <para>各 Hub 组件（realtime、notifications）都调用本方法而不是各自
    /// <c>AddSignalR()</c>：过滤器是全局的，注册两次会让每次调用建立两层上下文。
    /// 本方法幂等——重复调用只保留一个过滤器。</para>
    /// <para>不配置 <c>HubOptions</c>（心跳、超时、详细错误）：那是 SignalR 自身的选项，
    /// 宿主用 <c>AddSignalR(o =&gt; ...)</c> 或 <c>Configure&lt;HubOptions&gt;</c> 直接配，
    /// 框架再套一层只会覆盖宿主已配的值。</para>
    /// <para>本方法自己补齐 Hub 调用所需的非 HTTP 环境上下文，宿主无需先注册。
    /// <c>AddSecurity()</c> 只在宿主还有 Controller/HTTP 路径、需要从 <c>HttpContext.User</c>
    /// 读主体时才注册——它会把主体来源换成 HttpContext，与本方法的调用顺序无关。</para>
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

        // 幂等：realtime 与 notifications 都会调本方法，而 HubOptions.Filters 不可读，
        // 注册两次就会让每次调用建立两层上下文并复评两遍。用标记服务判重，
        // 与 ServiceUserContext 的注册守卫同一写法。
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
    // TryAdd 不行：AddSignalR() 内部已 TryAdd 了 DefaultUserIdProvider，而宿主往往先自己调
    // AddSignalR(o => ...) 配心跳，于是框架的 TryAdd 永远是空操作，UserIdClaimTypes 成为
    // 永不生效的摆设——DefaultUserIdProvider 只认 ClaimTypes.NameIdentifier，只签 sub 的主体
    // 解析不出标识，按用户寻址的推送全部落空。
    //
    // Replace 也不行：它会连宿主显式注册的实现一起顶掉，而 IUserIdProvider 是 SignalR 的
    // 公开扩展点。且 Replace 移除的是第一条、追加到末尾，[默认, 宿主] 会被改写成 [宿主, 本框架]，
    // 反而让本框架的胜出。
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
