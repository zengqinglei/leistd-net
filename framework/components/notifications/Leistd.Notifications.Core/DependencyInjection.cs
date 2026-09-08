using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Notifications.Services;
using Leistd.Notifications.Abstractions;

namespace Leistd.Notifications;

/// <summary>
/// 提供通知核心服务注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册通知发布核心能力。
    /// </summary>
    /// <remarks>
    /// 还需要一个 <c>INotificationStore</c> 实现（如 <c>AddNotificationsEfCore&lt;TDbContext&gt;()</c>）：
    /// 它是发布器的必需依赖，缺失时解析 <c>INotificationPublisher</c> 直接失败，而不是静默只推不落。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddNotifications();
    /// // 宿主还须注册一个 INotificationStore 实现（见通知家族文档），否则解析发布器即失败
    ///
    /// // 实时投递是可选关注点，单独注册
    /// await notificationPublisher.PublishToUserAsync(userId, new NotificationInputDto
    /// {
    ///     Title = "审批通过", Type = NotificationTypes.Workflow,
    /// }, ct);
    /// </code>
    /// </example>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        // 幂等：通知与实时两个包都会经各自注册入口调到这里，宿主两个都装是常态。
        // 不幂等会让 INotificationPublisher 出现两条，按 IEnumerable 解析时重复发布。
        services.TryAddTransient<INotificationPublisher, NotificationPublisher>();
        return services;
    }
}
