using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.MultiTenancy;

/// <summary>
/// Web 宿主多租户注册与管道挂载
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 配置节名：<c>Leistd:MultiTenancy</c>
    /// </summary>
    public const string ConfigurationSection = "Leistd:MultiTenancy";

    /// <summary>
    /// 注册多租户 Web 集成（含核心服务），从 <c>Leistd:MultiTenancy</c> 配置节绑定选项
    /// </summary>
    public static IServiceCollection AddMultiTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MultiTenancyOptions>(configuration.GetSection(ConfigurationSection));
        return services.AddMultiTenancyInternal();
    }

    /// <summary>
    /// 注册多租户 Web 集成（含核心服务）
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

        // 默认解析链仅在链为空时装配；宿主可在其后的 Configure 中增删排序
        services.Configure<TenantResolveOptions>(options =>
        {
            if (options.Contributors.Count == 0)
            {
                options.Contributors.Add(new CurrentPrincipalTenantResolveContributor());
                options.Contributors.Add(new HeaderTenantResolveContributor());
                options.Contributors.Add(new QueryStringTenantResolveContributor());
            }
        });

        return services;
    }

    /// <summary>
    /// 挂载多租户中间件。置于 <c>UseAuthentication()</c>（及 <c>UseServiceUserContext()</c>）之后、
    /// <c>UseAuthorization()</c> 之前
    /// </summary>
    public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder app)
    {
        return app.UseMiddleware<MultiTenancyMiddleware>();
    }
}
