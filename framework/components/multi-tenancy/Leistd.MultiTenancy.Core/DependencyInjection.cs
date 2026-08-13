using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.MultiTenancy;

/// <summary>
/// 多租户核心服务注册
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册多租户环境上下文与解析器（平台无关部分）
    /// </summary>
    /// <remarks>
    /// 不注册 <see cref="ITenantStore"/>——持有租户注册表的宿主用
    /// <c>AddMultiTenancyEfCore&lt;TDbContext&gt;()</c>，
    /// 配置型场景用 <see cref="AddInMemoryTenantStore"/>。
    /// Web 宿主应使用 <c>Leistd.MultiTenancy.AspNetCore</c> 的 <c>AddMultiTenancy()</c>（内部调用本方法）。
    /// </remarks>
    public static IServiceCollection AddMultiTenancyCore(this IServiceCollection services)
    {
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.TryAddTransient<ICurrentTenant, CurrentTenant>();
        services.TryAddTransient<ITenantNormalizer, UpperInvariantTenantNormalizer>();
        services.TryAddScoped<ITenantResolver, TenantResolver>();
        return services;
    }

    /// <summary>
    /// 注册配置型租户存储（<see cref="InMemoryTenantStore"/>）
    /// </summary>
    public static IServiceCollection AddInMemoryTenantStore(
        this IServiceCollection services,
        Action<InMemoryTenantStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        services.TryAddSingleton<ITenantStore, InMemoryTenantStore>();
        return services;
    }
}
