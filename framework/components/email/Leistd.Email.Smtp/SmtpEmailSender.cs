using Leistd.Email.Abstractions;
using Leistd.Redaction;
using Leistd.Email.Smtp.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Leistd.Email.Smtp;

/// <summary>经 SMTP 发信的 <see cref="IEmailSender"/>。</summary>
/// <remarks>
/// <para>每次调用建立独立连接，连接、认证与投递异常原样传播，不自动重试。</para>
/// <para>每封信取一次 <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>，配置源重载后下一封信即用新值；
/// 新值同样经 <see cref="IValidateOptions{TOptions}"/> 校验，不合规时抛 <see cref="OptionsValidationException"/>。</para>
/// </remarks>
public sealed class SmtpEmailSender(
    IOptionsMonitor<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var current = options.CurrentValue;
        var mime = BuildMessage(message, current);

        using var client = new SmtpClient();
        await client.ConnectAsync(current.Host, current.Port, ResolveSocketOptions(current), cancellationToken);

        if (!string.IsNullOrWhiteSpace(current.Username))
        {
            // CurrentValue 经 SmtpOptionsValidator 校验，只有用户名、没有口令的配置到不了这里。
            await client.AuthenticateAsync(current.Username, current.Password!, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        // 收件人脱敏后再记（保留域名便于按域名聚合）；主题不脱敏，它是宿主给的文案，组件文档写明不要放个人数据。
        logger.LogInformation(
            "Email sent to {To} with subject {Subject}",
            TextRedactor.RedactEmail(message.To),
            message.Subject);
    }

    // 465 要求连接即 TLS，其余加密端口使用 STARTTLS。
    private static SecureSocketOptions ResolveSocketOptions(SmtpOptions current)
        => current.EnableSsl
            ? current.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

    internal static MimeMessage BuildMessage(EmailMessage message, SmtpOptions current)
    {
        var mime = new MimeMessage();

        // 自定义发件身份不混用配置中的默认署名。
        var (address, display) = message.FromAddress is { Length: > 0 }
            ? (message.FromAddress, message.FromName)
            : (current.DefaultFromAddress, current.DefaultFromName);

        mime.From.Add(new MailboxAddress(display ?? string.Empty, address));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart(message.IsBodyHtml ? TextFormat.Html : TextFormat.Plain)
        {
            Text = message.Body
        };

        return mime;
    }
}
