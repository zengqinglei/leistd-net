using Leistd.ServiceClient.AspNetCore.Claims;
using Leistd.ServiceClient.AspNetCore.Middlewares;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.ServiceClient.AspNetCore;

/// <summary>
/// 被调方服务用户上下文注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册服务用户上下文恢复，Options 绑定配置节 <c>Leistd:ServiceUserContext</c>。
    /// 同时注册 <see cref="ServiceUserContextClaimsTransformation"/>（在认证阶段恢复，
    /// 覆盖授权策略按 scheme 重认证的路径）。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">应用配置</param>
    public static IServiceCollection AddServiceUserContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceUserContextOptions>(configuration.GetSection("Leistd:ServiceUserContext"));
        return AddServiceUserContextCore(services);
    }

    /// <summary>
    /// 注册服务用户上下文恢复（委托配置版，省略委托时使用默认配置）。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置委托</param>
    public static IServiceCollection AddServiceUserContext(
        this IServiceCollection services,
        Action<ServiceUserContextOptions>? configureOptions = null)
    {
        services.Configure(configureOptions ?? (_ => { }));
        return AddServiceUserContextCore(services);
    }

    private static IServiceCollection AddServiceUserContextCore(IServiceCollection services)
    {
        // 已注册过：直接返回。否则第二次调用会把上一次的注册当成「宿主转换」再包一层，
        // 每多调一次多嵌套一层（逻辑幂等掩盖了这一点，但每请求要多跑一遍链）。
        // Options 配置在公共入口完成，早退不影响重复调用时的重新配置。
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ServiceUserContextClaimsTransformation)))
        {
            return services;
        }

        services.AddHttpContextAccessor();
        services.AddSingleton<ServiceUserContextClaimsTransformation>();

        // ASP.NET Core 的认证服务只消费单个 IClaimsTransformation，后注册者覆盖先注册者
        // （AddAuthentication 会预注册一个空实现）。直接 Replace 会静默删掉宿主已注册的转换
        // （租户、外部身份等 claims 富化），因此把既有实现包进组合：宿主在前、恢复在后。
        // 宿主若在本方法**之后**才注册自己的转换，仍会覆盖本组合——那时由宿主负责组合，
        // ServiceUserContextClaimsTransformation 是公共类型，可直接注入调用（见组件文档）。
        var existing = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IClaimsTransformation));
        if (existing is null)
        {
            services.AddSingleton<IClaimsTransformation>(provider =>
                provider.GetRequiredService<ServiceUserContextClaimsTransformation>());
            return services;
        }

        // 组合沿用被包装服务的生命周期：宿主常把 claims 转换注册为 Scoped（它往往依赖
        // 请求级服务）。固定 Singleton 会把 scoped 依赖提升为单例——ValidateScopes 下直接
        // 抛异常，未开启校验时则跨请求捕获状态。同生命周期下工厂拿到的就是对应作用域的
        // provider，内层解析自然正确。
        //
        // 释放语义一并接管：内层由本组合创建后 DI 不再跟踪它，所有权转移给组合（见
        // CompositeClaimsTransformation）；宿主自行 new 的实例容器本就不拥有，不接管。
        var ownsInner = existing.ImplementationInstance is null;
        services.Remove(existing);
        services.Add(new ServiceDescriptor(
            typeof(IClaimsTransformation),
            provider => new CompositeClaimsTransformation(
                CreateInner(provider, existing),
                provider.GetRequiredService<ServiceUserContextClaimsTransformation>(),
                ownsInner),
            existing.Lifetime));
        return services;
    }

    /// <summary>
    /// 按原注册描述符还原宿主既有的转换实例（实现类型 / 工厂 / 单例三种注册形态）。
    /// <paramref name="provider"/> 是与原注册同生命周期的作用域 provider，
    /// 因此实现类型的构造依赖按其原本的作用域解析。
    /// </summary>
    private static IClaimsTransformation CreateInner(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IClaimsTransformation instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (IClaimsTransformation)factory(provider);
        }

        return (IClaimsTransformation)ActivatorUtilities.CreateInstance(
            provider, descriptor.ImplementationType!);
    }

    /// <summary>
    /// 启用服务用户上下文恢复中间件。必须置于 <c>UseAuthentication()</c> 之后、
    /// <c>UseAuthorization()</c> 之前——信任判定依赖已认证的调用方主体。
    /// </summary>
    /// <param name="app">应用构建器</param>
    public static IApplicationBuilder UseServiceUserContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ServiceUserContextMiddleware>();
    }
}
