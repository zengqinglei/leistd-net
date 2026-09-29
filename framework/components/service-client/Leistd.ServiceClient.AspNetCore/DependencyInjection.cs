using Leistd.ServiceClient.AspNetCore.Claims;
using Leistd.ServiceClient.AspNetCore.Middlewares;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.ServiceClient.AspNetCore;

/// <summary>
/// 被调方服务用户上下文注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册服务用户上下文恢复：绑定配置节，再应用宿主的编程式配置（代码覆盖配置文件）。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">编程式配置，在配置节绑定之后应用</param>
    /// <param name="configSectionPath">配置节路径，默认 <c>Leistd:ServiceUserContext</c></param>
    /// <example>
    /// <code>
    /// builder.Services.AddServiceUserContext();
    ///
    /// app.UseAuthentication();
    /// app.UseServiceUserContext();
    /// app.UseAuthorization();
    /// </code>
    /// </example>
    public static IServiceCollection AddServiceUserContext(
        this IServiceCollection services,
        Action<ServiceUserContextOptions>? configure = null,
        string configSectionPath = ServiceUserContextOptions.SectionName)
    {
        var options = services.AddOptions<ServiceUserContextOptions>().BindConfiguration(configSectionPath);
        if (configure is not null)
        {
            options.Configure(configure);
        }

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
