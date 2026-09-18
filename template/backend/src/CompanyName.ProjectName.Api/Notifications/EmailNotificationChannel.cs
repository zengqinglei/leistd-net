#if (LocalIdentity)
#if (IncludeNotifications)
using CompanyName.ProjectName.Api.HostedServices.Workers;
using CompanyName.ProjectName.Application.Notifications;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Email.Abstractions;
using Leistd.Notifications.Abstractions;
using Leistd.Notifications.Dtos;

namespace CompanyName.ProjectName.Api.Notifications;

/// <summary>
/// 邮件通知渠道：只发到收件人<b>已验证</b>的邮箱，经后台队列发送。
/// </summary>
/// <remarks>
/// <para>没验证过的邮箱不发：那个地址可能根本不是本人的，发过去就是把账号动态告诉了别人。</para>
/// <para>排进进程内后台队列（<see cref="IBackgroundTaskQueue"/>）而不是当场发：SMTP 往返常常要几秒，
/// 通知又经常发生在登录、改密码这类交互请求里。代价是进程在发送前退出时这封信会丢——
/// 通知是尽力而为的，站内那份才是记录。队列满时不等待、直接放弃这一封，不让触发通知的请求被拖住。</para>
/// </remarks>
public sealed class EmailNotificationChannel(
    IRepository<User, Guid> userRepository,
    IBackgroundTaskQueue backgroundTasks,
    ILogger<EmailNotificationChannel> logger) : INotificationChannel
{
    /// <inheritdoc />
    public string Name => AppNotificationChannels.Email;

    /// <inheritdoc />
    public async Task DeliverAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var id) ||
            await userRepository.GetByIdAsync(id, ct) is not { EmailConfirmed: true } user)
        {
            return;
        }

        var message = new EmailMessage
        {
            To = user.Email,
            Subject = notification.Title,
            Body = System.Net.WebUtility.HtmlEncode(notification.Content ?? notification.Title),
            IsBodyHtml = true
        };

        if (!backgroundTasks.TryQueue((services, token) =>
                new ValueTask(services.GetRequiredService<IEmailSender>().SendAsync(message, token))))
        {
            logger.LogWarning("Background queue is full; notification email to user {UserId} was dropped", userId);
        }
    }
}
#endif
#endif
