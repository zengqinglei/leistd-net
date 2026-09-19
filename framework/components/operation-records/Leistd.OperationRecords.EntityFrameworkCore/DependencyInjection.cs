using Leistd.DependencyInjection.Extensions;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore.EntityConfigurations;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.OperationRecords.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.OperationRecords.EntityFrameworkCore;

/// <summary>
/// 操作记录 EF Core 持久化依赖注入与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 EF Core 操作记录存储（基于指定 DbContext）。
    /// </summary>
    /// <remarks>
    /// 宿主须注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>；
    /// 本存储通过 <c>IDbContextProvider&lt;TDbContext&gt;</c> 获取绑定连接的上下文。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOperationRecordsEfCore&lt;AppDbContext&gt;();
    ///
    /// // DbContext 里映射操作记录表
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     base.OnModelCreating(modelBuilder);
    ///     modelBuilder.ConfigureOperationRecords();
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="TDbContext">承载操作记录表的 DbContext。</typeparam>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddOperationRecordsEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        // 操作记录只有一个权威存储：两个上下文各注册一次时会静默取一条，
        // 于是一半的审计写进了宿主没预期的库——而审计缺了一半比没有审计更危险。
        services.EnsureSingleAuthoritative<IOperationRecordStore, EfCoreOperationRecordStore<TDbContext>>(
            ServiceLifetime.Transient,
            "Operation records have a single authoritative store; map OperationRecord in one DbContext.");

        services.AddOperationRecords();
        services.TryAddTransient<IOperationRecordStore, EfCoreOperationRecordStore<TDbContext>>();

        return services;
    }

    /// <summary>
    /// 将 <see cref="OperationRecord"/> 实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    /// <param name="modelBuilder">模型构建器。</param>
    public static ModelBuilder ConfigureOperationRecords(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OperationRecordConfiguration());
        return modelBuilder;
    }
}
