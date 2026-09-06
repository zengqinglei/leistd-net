using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Leistd.AmbientContext;
using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.MultiTenancy.AspNetCore.Middlewares;
using Leistd.MultiTenancy.AspNetCore.AmbientContext;
using Leistd.MultiTenancy.AspNetCore.Resolution;
using Leistd.MultiTenancy.Resolution;

namespace Leistd.MultiTenancy.AspNetCore;

/// <summary>
/// 提供 ASP.NET Core 多租户注册和管道扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 获取默认配置节名称 <c>Leistd:MultiTenancy</c>。
    /// </summary>
    public const string ConfigurationSection = "Leistd:MultiTenancy";

    /// <summary>
    /// 从默认配置节注册多租户 Web 集成。
    /// </summary>
    /// <example>
    /// <code>
    /// // 宿主服务：持有租户注册表，解析后校验
    /// builder.Services.AddMultiTenancy(options =&gt; options.DomainFormat = "{0}.example.com");
    ///
    /// // 资源服务：只消费已验证令牌的 claim，无注册表
    /// builder.Services.AddMultiTenancy(options =&gt; options.ValidateResolvedTenant = false);
    ///
    /// // UseAuthentication() 之后、UseAuthorization() 之前
    /// app.UseMultiTenancy();
    /// </code>
    /// </example>
    public static IServiceCollection AddMultiTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MultiTenancyOptions>(configuration.GetSection(ConfigurationSection));
        return services.AddMultiTenancyInternal();
    }

    /// <summary>
    /// 使用委托配置注册多租户 Web 集成。
    /// </summary>
    public static IServiceCollection AddMultiTenancy(this IServiceCollection services, Action<MultiTenancyOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.AddMultiTenancyInternal();
    }

    private static IServiceCollection AddMultiTenancyInternal(this IServiceCollection services)
    {
        services.AddMultiTenancyCore();
        services.AddHttpContextAccessor();

        // 非 HTTP 入口的租户维度：Hub 调用、后台作业不经本组件的中间件，
        // 由环境上下文原语按主体的租户声明建立 ICurrentTenant。
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IAmbientContextContributor, TenantAmbientContextContributor>());

        // 域名是匿名请求的权威来源，格式错误必须在启动时失败。
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MultiTenancyOptions>, MultiTenancyOptionsValidator>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MultiTenancyOptions>, TenantStoreRegistrationValidator>());

        services.AddOptions<MultiTenancyOptions>().ValidateOnStart();

        // 仅为空链装配默认贡献者，保留宿主自定义顺序。
        services.AddOptions<TenantResolveOptions>()
            .Configure(options =>
            {
                if (options.Contributors.Count > 0)
                {
                    return;
                }

                options.Contributors.Add(new CurrentPrincipalTenantResolveContributor());

                // 已配置域名时优先于外部可控的请求头和查询串。
                options.Contributors.Add(new DomainTenantResolveContributor());
                options.Contributors.Add(new HeaderTenantResolveContributor());
                options.Contributors.Add(new QueryStringTenantResolveContributor());
            });

        // 关闭注册表校验时在最终配置阶段强制只信主体 claim。
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<TenantResolveOptions>,
                PrincipalOnlyTenantResolveEnforcer>());

        return services;
    }

    /// <summary>
    /// 在认证和受信上下文恢复之后、授权之前挂载多租户中间件。
    /// </summary>
    /// <remarks>
    /// <c>ValidateResolvedTenant</c> 为 <see langword="true"/> 时校验注册表；为
    /// <see langword="false"/> 时只信已认证主体的租户声明。
    /// </remarks>
    public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder app)
    {
        return app.UseMiddleware<MultiTenancyMiddleware>();
    }

}
