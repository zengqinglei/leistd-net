using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Notifications.EntityFrameworkCore.EntityConfigurations;
using Leistd.Notifications.EntityFrameworkCore.Stores;
using Leistd.Notifications.Abstractions;

namespace Leistd.Notifications.EntityFrameworkCore;

/// <summary>
/// 通知 EF Core 持久化依赖注入与模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 EF Core 通知持久化存储（基于指定 DbContext）。
    /// </summary>
    /// <remarks>
    /// 宿主须注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>；
    /// 本存储通过 <c>IDbContextProvider&lt;TDbContext&gt;</c> 获取绑定连接的上下文。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddNotificationsEfCore&lt;AppDbContext&gt;();
    ///
    /// // DbContext 里映射通知表
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     base.OnModelCreating(modelBuilder);
    ///     modelBuilder.ConfigureNotifications();
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddNotificationsEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {

        // 发布器只消费一个权威存储：两个上下文各注册一次时会静默取一条，通知落进宿主没预期的库。
        services.EnsureSingleAuthoritative<INotificationStore, EfCoreNotificationStore<TDbContext>>(
            ServiceLifetime.Transient,
            "Notifications have a single authoritative store; map NotificationRecord in one DbContext.");

        services.TryAddTransient<INotificationStore, EfCoreNotificationStore<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 将 NotificationRecord 实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    public static ModelBuilder ConfigureNotifications(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new NotificationRecordConfiguration());
        return modelBuilder;
    }

}
