using Leistd.Notifications.Channels;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Publishing;
using Leistd.Notifications.Stores;
using Leistd.Notifications.Email.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Notifications.Email;

/// <summary>
/// 通知邮件渠道的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 登记邮件渠道 <see cref="EmailNotificationChannel"/>。
    /// </summary>
    /// <remarks>
    /// 宿主还需注册 <c>INotificationRecipientResolver</c>（从自己的用户模型取已验证地址）、一个 <c>IEmailSender</c>
    /// 与后台任务队列（如 <c>AddInProcessBackgroundJobs()</c>）。启用通知偏好时，渠道段取 <see cref="EmailNotificationChannel.ChannelName"/>。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddEmailNotifications();
    /// builder.Services.AddScoped&lt;INotificationRecipientResolver, UserEmailRecipientResolver&gt;();
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddEmailNotifications(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationChannel, EmailNotificationChannel>());
        return services;
    }
}
