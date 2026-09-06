using Leistd.ServiceClient.AspNetCore.Claims;
using Leistd.ServiceClient.AspNetCore.Middlewares;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.ServiceClient.AspNetCore;

/// <summary>
/// 被调方服务用户上下文注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 从 <c>Leistd:ServiceUserContext</c> 绑定选项并注册服务用户上下文恢复。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">应用配置</param>
    /// <example>
    /// <code>
    /// builder.Services.AddServiceUserContext(builder.Configuration);
    ///
    /// app.UseAuthentication();
    /// app.UseServiceUserContext();
    /// app.UseAuthorization();
    /// </code>
    /// </example>
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

    // 独立标记区分本扩展的注册与宿主自行注册的转换器。
    private sealed class ServiceUserContextRegistrationMarker;

    private static IServiceCollection AddServiceUserContextCore(IServiceCollection services)
    {
        // 避免重复调用时递归包装已组合的转换器。
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ServiceUserContextRegistrationMarker)))
        {
            return services;
        }

        services.AddSingleton<ServiceUserContextRegistrationMarker>();
        services.AddHttpContextAccessor();
        // 组件内部依赖固定为单例；TryAdd 可能接受不兼容的宿主生命周期。
        services.AddSingleton<ServiceUserContextClaimsTransformation>();

        // 保留宿主的默认转换器；keyed 注册不属于认证管道消费的服务。
        var existing = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IClaimsTransformation) && !descriptor.IsKeyedService);
        if (existing is null)
        {
            services.AddSingleton<IClaimsTransformation>(provider =>
                provider.GetRequiredService<ServiceUserContextClaimsTransformation>());
            return services;
        }

        // 沿用原生命周期以保留作用域依赖；仅接管由组合创建的内层实例。
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

    // 按原描述符创建实例，保留实现类型、工厂和现有实例的语义。
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
