using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Timing;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.EntityConfigurations;
using Leistd.MultiTenancy.EntityFrameworkCore.Managers;
using Leistd.MultiTenancy.EntityFrameworkCore.Stores;
using Leistd.MultiTenancy.Stores;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 提供多租户 EF Core 存储注册和模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 使用指定 DbContext 注册租户和连接配置存储。
    /// </summary>
    /// <remarks>
    /// 不注册租户落值组件；DDD 基座在实体进入跟踪时写入 <c>TenantId</c>。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddMultiTenancyEfCore&lt;ControlDbContext&gt;();
    ///
    /// // 控制面上下文没有软删除过滤器，查询必须使用未删除入口。
    /// var tenants = await controlDb.UndeletedTenants().ToListAsync(ct);
    /// </code>
    /// </example>
    public static IServiceCollection AddMultiTenancyEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddMultiTenancyCore();
        services.TryAddSingleton<IClock, UtcClockProvider>();
        services.TryAddTransient<ITenantStore, EfCoreTenantStore<TDbContext>>();
        services.TryAddTransient<ITenantManager, EfCoreTenantManager<TDbContext>>();
        services.TryAddTransient<ITenantConnectionConfigurationStore,
            EfCoreTenantConnectionConfigurationStore<TDbContext>>();
        services.TryAddTransient<ITenantConnectionConfigurationManager,
            EfCoreTenantConnectionConfigurationManager<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 将租户注册表实体映射应用到模型。
    /// </summary>
    /// <remarks>
    /// 不添加查询过滤器；读取注册表必须使用 <c>TenantQueryableExtensions</c> 的未删除入口。
    /// </remarks>
    public static ModelBuilder ConfigureMultiTenancy(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TenantRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConnectionRecordConfiguration());
        return modelBuilder;
    }
}
