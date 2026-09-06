using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    /// <b>前置</b>：宿主须已注册 <c>AddUnitOfWork()</c> 与 <c>AddUnitOfWorkEfCore()</c>——
    /// 本家族的存储与管理器经 <c>IDbContextProvider&lt;TDbContext&gt;</c> 取上下文
    /// （只有它会设置 <c>DbContextCreationContext.Current</c>，从而拿到本工作单元已解析的连接）。
    /// 与 <c>AddMultiTenancyEfCore</c> 同一约定：组件不替其它组件注册基础设施。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddNotificationsEfCore&lt;AppDbContext&gt;();
    ///
    /// // DbContext 里映射通知表
    /// protected override void ConfigureModel(ModelBuilder modelBuilder)
    ///     =&gt; modelBuilder.ConfigureNotifications();
    /// </code>
    /// </example>
    public static IServiceCollection AddNotificationsEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        var implementationType = typeof(EfCoreNotificationStore<TDbContext>);

        // 两个上下文各注册一次时 Microsoft DI 静默取最后一条：通知会落进宿主没预期的那个库，
        // 读回来的也是那一个，全程没有信号。发布器只消费一个权威存储，这里直接拒绝。
        // 重复登记同一个上下文是无害的，按幂等处理。
        if (services.FirstOrDefault(d => d.ServiceType == typeof(INotificationStore)) is { } existing
            && existing.ImplementationType != implementationType)
        {
            throw new InvalidOperationException(
                $"An {nameof(INotificationStore)} is already registered as " +
                $"'{existing.ImplementationType?.FullName ?? "<factory>"}'. Notifications have a single " +
                $"authoritative store; registering '{implementationType.FullName}' would silently win by " +
                "ordering and user notifications would be written to a different database than the caller " +
                "expects. Map NotificationRecord in one DbContext.");
        }

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
