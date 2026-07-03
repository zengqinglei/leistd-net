using Leistd.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// 权限 EF Core 持久化依赖注入与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 EF Core 权限授予存储（基于指定 DbContext）。
    /// </summary>
    public static IServiceCollection AddAuthorizationEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddAuthorizationCore();
        services.AddTransient<IPermissionGrantStore, EfCorePermissionGrantStore<TDbContext>>();
        services.AddTransient<IPermissionGrantManager, EfCorePermissionGrantManager<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 将 PermissionGrantRecord 实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    public static ModelBuilder ConfigureAuthorization(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PermissionGrantRecordConfiguration());
        return modelBuilder;
    }
}

