using Leistd.Timing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 多租户 EF Core 持久化依赖注入与模型配置
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 EF Core 租户存储、管理器与落值拦截器（基于指定 DbContext）
    /// </summary>
    /// <remarks>
    /// <para>依赖 <c>IDistributedCache</c>（租户配置缓存），宿主需已注册分布式缓存
    /// （内存或 Redis 均可）。</para>
    /// <para>落值拦截器需宿主经 <c>options.AddInterceptors(sp.GetRequiredService&lt;MultiTenantSaveChangesInterceptor&gt;())</c>
    /// 显式挂载到 DbContext。</para>
    /// </remarks>
    public static IServiceCollection AddMultiTenancyEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddMultiTenancyCore();
        services.TryAddSingleton<IClock, UtcClockProvider>();
        services.TryAddTransient<ITenantStore, EfCoreTenantStore<TDbContext>>();
        services.TryAddTransient<ITenantManager, EfCoreTenantManager<TDbContext>>();
        services.AddTransient<MultiTenantSaveChangesInterceptor>();
        return services;
    }

    /// <summary>
    /// 将租户注册表实体配置应用到 DbContext。在 OnModelCreating 中调用
    /// </summary>
    public static ModelBuilder ConfigureMultiTenancy(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TenantRecordConfiguration());
        return modelBuilder;
    }
}
