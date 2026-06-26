using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Notifications.EntityFrameworkCore;

/// <summary>
/// 通知 EF Core 持久化依赖注入与模型配置扩展。
/// </summary>
public static class NotificationsEfCoreServiceCollectionExtensions
{
    /// <summary>
    /// 注册 EF Core 通知持久化存储（基于指定 DbContext）。
    /// </summary>
    public static IServiceCollection AddNotificationsEfCoreStore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<INotificationStore, EfCoreNotificationStore<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 将 NotificationRecord 实体配置应用到 DbContext。在 OnModelCreating 中调用。
    /// </summary>
    public static ModelBuilder ApplyLeistdNotificationsConfiguration(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new NotificationRecordConfiguration());
        return modelBuilder;
    }
}
