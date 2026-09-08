using Leistd.Email.Abstractions;
using Microsoft.Extensions.Logging;

namespace Leistd.Email;

/// <summary>
/// 不投递、只记录一条未投递告警的 <see cref="IEmailSender"/>。
/// </summary>
/// <remarks>
/// 供没有可用 SMTP 的环境（本地开发、演示）使用，必须由宿主<b>显式</b>注册。
/// <para><b>只记录收件人与主题，不记录正文。</b>因此它不能用来取验证码或密码重置链接——
/// 那类内容进应用日志等于把凭据留在日志里。需要看到邮件内容时改配
/// <c>AddSmtpEmailSender</c> 指向本机邮件捕获器（如 Mailpit），在它的收件箱里看。</para>
/// <para>它<b>不是</b>发送失败时的兜底：任何"配置看起来不对就悄悄不发"的回落都会让
/// 调用方以为信已发出。因此本类只在被主动注册时生效，且按
/// <see cref="LogLevel.Warning"/> 记录——降级成 Debug 会让误配的生产环境静默丢信。</para>
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
