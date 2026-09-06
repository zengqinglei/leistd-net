using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Authorization.Resource.EntityFrameworkCore.EntityConfigurations;
using Leistd.Authorization.Resource.EntityFrameworkCore.Managers;
using Leistd.Authorization.Resource.EntityFrameworkCore.Stores;
using Leistd.Authorization.Resource.Grants;

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
    /// <remarks>
    /// <b>前置</b>：宿主须已注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>——
    /// 本家族的存储与管理器经 <c>IDbContextProvider&lt;TDbContext&gt;</c> 取上下文
    /// （只有它会设置 <c>DbContextCreationContext.Current</c>，从而拿到本工作单元已解析的连接）。
    /// 与 <c>AddMultiTenancyEfCore</c> 同一约定：组件不替其它组件注册基础设施。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddResourceAuthorizationEfCore&lt;AppDbContext&gt;();   // 已内含 Core 注册
    /// </code>
    /// </example>
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
        modelBuilder.ApplyConfiguration(new ResourceAuthorizationVersionRecordConfiguration());
        return modelBuilder;
    }
}
