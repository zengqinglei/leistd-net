using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Authorization.Resource.EntityFrameworkCore;

/// <summary>
/// 资源 ACL 的 EF Core 持久化依赖注入与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册基于指定 DbContext 的资源 ACL 存储与管理器。
    /// </summary>
    /// <remarks>内部已调用 <c>AddResourceAuthorizationCore()</c>。</remarks>
    public static IServiceCollection AddResourceAuthorizationEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddResourceAuthorizationCore();
        services.AddScoped<IResourceGrantStore, EfCoreResourceGrantStore<TDbContext>>();
        services.AddScoped<IResourceGrantManager, EfCoreResourceGrantManager<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 将资源 ACL 实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    public static ModelBuilder ConfigureResourceAuthorization(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ResourcePermissionGrantRecordConfiguration());
        return modelBuilder;
    }
}
