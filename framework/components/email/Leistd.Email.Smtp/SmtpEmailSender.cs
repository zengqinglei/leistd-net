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
/// 任何失败（连不上、认证失败、被拒收）都原样抛出。本类不含"配置看起来不对就跳过发送"
/// 的分支——那种回落会让调用方以为信已发出；没有可用 SMTP 时请注册
/// <see cref="NullEmailSender"/>。
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
            // 用户名与口令成对，由 SmtpOptionsValidator 在启动期保证。这里把不变量重述一遍
            // 不是防御性冗余：取到一半说明校验器没接上，而"跳过认证继续发送"会让服务器
            // 按匿名中继接受或拒收，两种都不指向真正的原因。
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

    // 465 是隐式 TLS（连上即握手），其余端口走 STARTTLS。两者不可互换：
    // 对 465 用 StartTls 会卡在等待明文问候，对 587 用 SslOnConnect 会握手失败。
    private static SecureSocketOptions ResolveSocketOptions(SmtpOptions current)
        => current.EnableSsl
            ? current.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

    internal static MimeMessage BuildMessage(EmailMessage message, SmtpOptions current)
    {
        var mime = new MimeMessage();

        // 发件地址与显示名成对取用：给了地址就按给的显示名（可为空），
        // 不与配置默认值交叉拼装，避免"自定义地址 + 默认署名"。
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
