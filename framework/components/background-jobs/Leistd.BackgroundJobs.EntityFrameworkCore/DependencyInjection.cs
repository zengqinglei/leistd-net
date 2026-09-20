using Leistd.BackgroundJobs.EntityFrameworkCore.EntityConfigurations;
using Leistd.BackgroundJobs.EntityFrameworkCore.Entities;
using Leistd.BackgroundJobs.EntityFrameworkCore.Stores;
using Leistd.BackgroundJobs.Recurring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.BackgroundJobs.EntityFrameworkCore;

/// <summary>
/// 周期任务水位的 EF Core 存储注册与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 用指定 DbContext 存储集群周期任务的完成水位，替换进程内默认实现。
    /// </summary>
    /// <remarks>
    /// 多副本部署必须注册：只有共享的水位能挡住"副本 A 做完、时钟稍慢的副本 B 在同一时段又做一遍"。
    /// 与调度器注册的先后无关，本方法总是成为唯一的水位存储。宿主须已注册 <c>AddUnitOfWork()</c> 与
    /// <c>AddUnitOfWorkEfCore()</c>，并在 <c>OnModelCreating</c> 里调用 <see cref="ConfigureBackgroundJobs"/>。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddBackgroundJobsEfCore&lt;AppDbContext&gt;();
    ///
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     modelBuilder.ConfigureBackgroundJobs();
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="TDbContext">承载水位表的 DbContext。</typeparam>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddBackgroundJobsEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.RemoveAll<IRecurringJobStateStore>();
        services.AddTransient<IRecurringJobStateStore, EfCoreRecurringJobStateStore<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 将 <see cref="RecurringJobState"/> 的实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    /// <param name="modelBuilder">模型构建器。</param>
    public static ModelBuilder ConfigureBackgroundJobs(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RecurringJobStateConfiguration());
        return modelBuilder;
    }
}
