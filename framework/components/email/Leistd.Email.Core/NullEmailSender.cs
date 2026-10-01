using Leistd.Email.Abstractions;
using Leistd.Redaction;
using Microsoft.Extensions.Logging;

namespace Leistd.Email;

/// <summary>
/// 不投递、只记录一条未投递告警的 <see cref="IEmailSender"/>。
/// </summary>
/// <remarks>
/// 必须显式注册，调用成功不表示邮件已投递。
/// 按 <see cref="LogLevel.Warning"/> 记录<b>脱敏后的</b>收件人与主题，不记录可能包含验证码或重置链接的正文。
/// 需要看完整地址或正文时用 SMTP 邮件捕获器——本类是给"没配 SMTP"的形态兜底的，
/// 而它一旦被误注册到生产，日志里就会留下每一个收件人，所以这里也脱敏。
/// </remarks>
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogWarning(
            "NullEmailSender is registered: no email was actually sent. To={To} Subject={Subject}",
            TextRedactor.RedactEmail(message.To),
            message.Subject);

        return Task.CompletedTask;
    }
}
