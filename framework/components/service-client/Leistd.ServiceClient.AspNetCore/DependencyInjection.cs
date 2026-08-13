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
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ServiceUserContextClaimsTransformation>();

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

        services.Remove(existing);
        services.AddSingleton<IClaimsTransformation>(provider => new CompositeClaimsTransformation(
            CreateInner(provider, existing),
            provider.GetRequiredService<ServiceUserContextClaimsTransformation>()));
        return services;
    }

    /// <summary>
    /// 按原注册描述符还原宿主既有的转换实例（实现类型 / 工厂 / 单例三种注册形态）。
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
