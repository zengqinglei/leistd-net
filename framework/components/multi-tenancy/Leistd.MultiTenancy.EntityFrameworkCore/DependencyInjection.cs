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
    /// 注册 EF Core 租户注册表存储与管理器（基于指定 DbContext）
    /// </summary>
    /// <remarks>
    /// <para><b>不注册任何落值组件。</b>新增实体的 <c>TenantId</c> 由 <c>BaseDbContext</c>
    /// 在实体进入变更跟踪时落定（与创建审计同一钩子），无需挂载拦截器。
    /// 落值放在保存时刻会让租户值随工作单元的事务边界漂移。</para>
    /// </remarks>
    public static IServiceCollection AddMultiTenancyEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddMultiTenancyCore();
        services.TryAddSingleton<IClock, UtcClockProvider>();
        services.TryAddTransient<ITenantStore, EfCoreTenantStore<TDbContext>>();
        services.TryAddTransient<ITenantManager, EfCoreTenantManager<TDbContext>>();
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
