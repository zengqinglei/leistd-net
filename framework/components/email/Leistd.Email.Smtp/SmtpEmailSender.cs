using Leistd.Email.Abstractions;
using Leistd.Email.Smtp.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Leistd.Email.Smtp;

/// <summary>
/// 经 SMTP 发信的 <see cref="IEmailSender"/>。
/// </summary>
/// <remarks>
/// 每次调用建立独立连接，连接、认证与投递异常原样传播，不自动重试。
/// </remarks>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var current = options.Value;
        var mime = BuildMessage(message, current);

        using var client = new SmtpClient();
        await client.ConnectAsync(current.Host, current.Port, ResolveSocketOptions(current), cancellationToken);

        if (!string.IsNullOrWhiteSpace(current.Username))
        {
            // 即使绕过启动校验，也不能把缺少口令误当作匿名投递。
            var password = current.Password
                ?? throw new InvalidOperationException(
                    $"{SmtpOptions.SectionName}: Username is set but Password is missing. " +
                    "Both must be provided together; startup validation should have rejected this.");

            await client.AuthenticateAsync(current.Username, password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Email sent to {To} with subject {Subject}", message.To, message.Subject);
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
