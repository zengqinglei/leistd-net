using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.MultiTenancy.Stores;
using Leistd.Data;
using Leistd.MultiTenancy.Resolution;
using Leistd.MultiTenancy.Services;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.Data.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy;

/// <summary>
/// 提供平台无关的多租户服务注册。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册当前租户上下文和租户解析器。
    /// </summary>
    /// <remarks>
    /// 不注册 <see cref="ITenantStore"/>；宿主必须选择 EF Core 或内存实现。
    /// ASP.NET Core 宿主应使用 Web 集成包的 <c>AddMultiTenancy()</c>。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddMultiTenancyCore();
    ///
    /// builder.Services.AddInMemoryTenantStore(store =&gt; store.Tenants.Add(
    ///     new TenantConfiguration { Id = tenantId, Name = "acme", NormalizedName = "ACME" }));
    /// </code>
    /// </example>
    public static IServiceCollection AddMultiTenancyCore(this IServiceCollection services)
    {
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.TryAddTransient<ICurrentTenant, CurrentTenant>();
        services.TryAddTransient<ITenantNormalizer, UpperInvariantTenantNormalizer>();
        // 连接相同的共享库租户仍必须绑定不同的工作单元归属。
        services.TryAddTransient<IConnectionAffinityProvider, TenantConnectionAffinityProvider>();
        services.TryAddScoped<ITenantResolver, TenantResolver>();
        return services;
    }

    /// <summary>
    /// 注册由选项提供租户清单的内存存储。
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
