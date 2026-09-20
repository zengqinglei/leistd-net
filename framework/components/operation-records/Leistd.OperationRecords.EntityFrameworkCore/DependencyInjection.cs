using Leistd.BackgroundJobs;
using Leistd.BackgroundJobs.Recurring;
using Leistd.DependencyInjection.Extensions;
using Leistd.OperationRecords.EntityFrameworkCore.Options;
using Leistd.OperationRecords.EntityFrameworkCore.Retention;
using Microsoft.Extensions.Options;
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
    /// <para>本存储通过 <c>IDbContextProvider&lt;TDbContext&gt;</c> 获取绑定连接的上下文，
    /// 因此宿主须注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>。</para>
    /// <para><b>记录器还要四样跨组件前置</b>，缺一个在首次解析 <c>IOperationRecorder</c> 时才暴露：
    /// <c>IClock</c>（宿主自行 <c>AddSingleton&lt;IClock, UtcClockProvider&gt;()</c>，
    /// <c>Leistd.Core</c> 刻意不提供 DI 扩展）、<c>ICurrentTenant</c>（<c>AddMultiTenancyCore()</c>）、
    /// <c>ICurrentUser</c>（<c>AddAmbientContext()</c>）、<c>ICorrelationIdProvider</c>
    /// （<c>AddCorrelationIdCore(configuration)</c>）。每条记录都要回答"谁、在哪个租户、哪条链路、什么时间"，
    /// 四样各来自一个独立组件；本组件<b>不</b>替调用方注册——组件由宿主显式组合。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddSingleton&lt;IClock, UtcClockProvider&gt;();
    /// builder.Services.AddMultiTenancyCore();
    /// builder.Services.AddAmbientContext();
    /// builder.Services.AddCorrelationIdCore(builder.Configuration);
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
    /// 启用保留期归档：到期记录按天搬入归档表，作为集群周期任务执行。
    /// </summary>
    /// <remarks>
    /// <para>选项绑定 <c>Leistd:OperationRecords:Retention</c> 并在启动期校验；默认 <c>Enabled = false</c>，
    /// 任务照常排期、到点跳过，打开开关下一轮即生效。</para>
    /// <para>需要后台作业调度器（如 <c>AddInProcessBackgroundJobs()</c>）与分布式锁；
    /// 归档按物理库逐个执行，独立库租户的记录在各自的库里归档。</para>
    /// <para>逐库遍历（<c>ITenantDatabaseRunner</c>）由 <c>AddMultiTenancyCore()</c> 提供——
    /// 基础注册本来就要它。不分库时它给出的清单只有宿主库，行为与单库一致。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOperationRecordsEfCore&lt;AppDbContext&gt;();   // 连同上面那四样前置
    /// builder.Services.AddOperationRecordRetention&lt;AppDbContext&gt;();
    /// </code>
    /// </example>
    /// <typeparam name="TDbContext">承载操作记录与归档表的 DbContext。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">在配置节之后应用的选项配置。</param>
    public static IServiceCollection AddOperationRecordRetention<TDbContext>(
        this IServiceCollection services,
        Action<OperationRecordRetentionOptions>? configure = null)
        where TDbContext : DbContext
    {
        services.AddOptions<OperationRecordRetentionOptions>()
            .BindConfiguration(OperationRecordRetentionOptions.SectionName)
            .ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OperationRecordRetentionOptions>, OperationRecordRetentionOptionsValidator>());
        services.TryAddTransient<IOperationRecordArchiveService, OperationRecordArchiveService<TDbContext>>();
        services.AddRecurringJob<OperationRecordArchiveJob>(
            OperationRecordArchiveJob.Name,
            sp => RecurringJobSchedule.DailyAt(new TimeOnly(
                sp.GetRequiredService<IOptions<OperationRecordRetentionOptions>>().Value.DailyRunHourUtc, 0)),
            RecurringJobScope.Cluster);
        return services;
    }

    /// <summary>
    /// 将 <see cref="OperationRecord"/> 与 <see cref="OperationRecordArchive"/> 的实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    /// <remarks>归档表随原表一起映射：启用保留期不需要改模型，也不会出现"开了归档却没有归档表"。</remarks>
    /// <param name="modelBuilder">模型构建器。</param>
    public static ModelBuilder ConfigureOperationRecords(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OperationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new OperationRecordArchiveConfiguration());
        return modelBuilder;
    }
}
