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
        // ASP.NET Core 的 AuthenticationService 只消费单个 IClaimsTransformation，且
        // AddAuthentication 会预注册 NoopClaimsTransformer——必须用 Replace 而非 TryAdd，
        // 否则本转换永远不生效。宿主另有自己的转换时需自行组合本恢复逻辑（见组件文档）。
        services.Replace(ServiceDescriptor.Singleton<IClaimsTransformation, ServiceUserContextClaimsTransformation>());
        return services;
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
