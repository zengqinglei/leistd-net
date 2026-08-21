using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        // DomainFormat 一旦配置就是匿名请求的权威来源，写错了必须启动期就失败——
        // 运行期它只表现为"永不匹配"，解析会静默退回请求头，边界没了却没人知道
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MultiTenancyOptions>, MultiTenancyOptionsValidator>());
        services.AddOptions<MultiTenancyOptions>().ValidateOnStart();

        // 默认解析链仅在链为空时装配；宿主可在其后的 Configure 中增删排序
        services.Configure<TenantResolveOptions>(options =>
        {
            if (options.Contributors.Count == 0)
            {
                options.Contributors.Add(new CurrentPrincipalTenantResolveContributor());

                // 子域名排在头与查询串之前：配置了 DomainFormat 的部署里域名就是权威，
                // 不允许匿名请求再用请求头把自己挪到别的租户。未配置时该贡献者直接跳过
                options.Contributors.Add(new DomainTenantResolveContributor());
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

    /// <summary>
    /// 挂载 Resource 宿主的可信租户上下文。置于 <c>UseAuthentication()</c> 之后、
    /// <c>UseAuthorization()</c> 之前，且不得与 <see cref="UseMultiTenancy"/> 同时使用。
    /// </summary>
    /// <remarks>
    /// 该入口只接受已验证主体中唯一合法的 <c>tenant_id</c>，不读取请求头、查询串、域名或
    /// <see cref="ITenantStore"/>。租户停用由签发端停止签发/刷新和 Access Token 生命周期收敛。
    /// </remarks>
    public static IApplicationBuilder UseAuthenticatedTenantContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuthenticatedTenantContextMiddleware>();
    }
}
