using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Notifications;

/// <summary>
/// 通知核心组件依赖注入配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册通知发布核心能力。
    /// </summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        return services;
    }
}
