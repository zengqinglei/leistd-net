using System.Net;
using Leistd.BackgroundJobs.Queues;
using Leistd.Email.Abstractions;
using Leistd.Notifications.Channels;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Publishing;
using Leistd.Notifications.Stores;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Email.Recipients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.Notifications.Email.Channels;

/// <summary>
/// 通知的邮件渠道：收件人有已验证地址时，经后台队列异步发送。
/// </summary>
/// <remarks>
/// <para>正文按纯文本 HTML 编码后发送：通知内容可能夹带用户可控文本，原样当 HTML 发出等于让邮件承载注入。</para>
/// <para>队列满时丢弃本封并记警告，不阻塞发布方——邮件是尽力而为的附加渠道，站内通知已经落库。</para>
/// </remarks>
/// <param name="recipients">收件人地址解析。</param>
/// <param name="queue">后台任务队列。</param>
/// <param name="logger">日志。</param>
public sealed class EmailNotificationChannel(
    INotificationRecipientResolver recipients,
    IBackgroundTaskQueue queue,
    ILogger<EmailNotificationChannel> logger) : INotificationChannel
{
    /// <summary>渠道名，也是通知偏好设置名里的渠道段。</summary>
    public const string ChannelName = "Email";

    /// <inheritdoc />
    public string Name => ChannelName;

    /// <inheritdoc />
    public async Task DeliverAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
    {
        if (await recipients.ResolveEmailAsync(userId, ct) is not { Length: > 0 } email)
        {
            return;
        }

        var message = new EmailMessage
        {
            To = email,
            Subject = notification.Title,
            Body = WebUtility.HtmlEncode(notification.Content ?? notification.Title),
            IsBodyHtml = true
        };

        if (!queue.TryQueue((services, token) => new ValueTask(services.GetRequiredService<IEmailSender>().SendAsync(message, token))))
        {
            logger.LogWarning("The background queue is full; the notification email to user {UserId} was dropped.", userId);
        }
    }
}
