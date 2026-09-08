using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Authorization.EntityFrameworkCore.EntityConfigurations;
using Leistd.Authorization.EntityFrameworkCore.Managers;
using Leistd.Authorization.EntityFrameworkCore.Stores;
using Leistd.Authorization.EntityFrameworkCore.Entities;
using Leistd.Authorization.Abstractions;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// 权限 EF Core 持久化依赖注入与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 EF Core 权限授予存储（基于指定 DbContext）。
    /// </summary>
    /// <remarks>
    /// <b>前置</b>：宿主须已注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>——
    /// 本家族的存储与管理器经 <c>IDbContextProvider&lt;TDbContext&gt;</c> 取上下文
    /// （只有它会设置 <c>DbContextCreationContext.Current</c>，从而拿到本工作单元已解析的连接）。
    /// 与 <c>AddMultiTenancyEfCore</c> 同一约定：组件不替其它组件注册基础设施。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAuthorizationEfCore&lt;AppDbContext&gt;();
    ///
    /// // 写入统一经管理器：自动补齐祖先、级联清理子孙
    /// await grantManager.GrantAsync("Orders.Update", PermissionGrantProviderNames.Role, "admin", ct);
    /// </code>
    /// </example>
    public static IServiceCollection AddAuthorizationEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        // 权限授予只有一个权威存储：两个上下文各注册一次时会静默取一条，授予写进/读自
        // 宿主没预期的那个库——症状是越权或全员 403，而不是报错。
        //
        // Manager 允许宿主替换，仅 Store 要求唯一权威实现。
        services.EnsureSingleAuthoritative<IPermissionGrantStore, EfCorePermissionGrantStore<TDbContext>>(
            ServiceLifetime.Transient,
            "Permission grants have a single authoritative store; map the authorization tables in one DbContext.");

        services.AddPermissionAuthorizationCore();
        services.TryAddTransient<IPermissionGrantStore, EfCorePermissionGrantStore<TDbContext>>();
        services.TryAddTransient<IPermissionGrantManager, EfCorePermissionGrantManager<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 将授权相关实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    /// <remarks>
    /// 同时映射 <see cref="PermissionGrantRecord"/> 与 <see cref="AuthorizationVersionRecord"/>；
    /// 两者缺一不可，版本表缺失会导致批量替换无法做乐观并发校验。
    /// </remarks>
    public static ModelBuilder ConfigureAuthorization(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PermissionGrantRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AuthorizationVersionRecordConfiguration());
        return modelBuilder;
    }
}
