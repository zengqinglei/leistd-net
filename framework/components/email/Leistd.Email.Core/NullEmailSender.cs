using Leistd.Email.Abstractions;
using Microsoft.Extensions.Logging;

namespace Leistd.Email;

/// <summary>
/// 不投递、只记录一条未投递告警的 <see cref="IEmailSender"/>。
/// </summary>
/// <remarks>
/// 必须显式注册，调用成功不表示邮件已投递。
/// 按 <see cref="LogLevel.Warning"/> 记录收件人与主题，不记录可能包含验证码或重置链接的正文。
/// 需要查看正文时应使用 SMTP 邮件捕获器。
/// </remarks>
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogWarning(
            "NullEmailSender is registered: no email was actually sent. To={To} Subject={Subject}",
            message.To,
            message.Subject);

        return Task.CompletedTask;
    }
}
