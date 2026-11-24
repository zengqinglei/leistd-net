using System.Net.Sockets;
using Leistd.Email.Abstractions;
using Leistd.Email.Exceptions;
using Leistd.Redaction;
using Leistd.Email.Smtp.Options;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Leistd.Email.Smtp;

/// <summary>经 SMTP 发信的 <see cref="IEmailSender"/>。</summary>
/// <remarks>
/// <para>每次调用建立独立连接，不自动重试。连接、认证与投递阶段能归类的失败抛 <see cref="EmailDeliveryException"/>，
/// MailKit 的原异常为内层；其余失败与取消原样传播。
/// 收到服务器的接受确认之后，断开连接被取消或超时都不改写发送结果；超时记 Warning。</para>
/// <para>每封信取一次 <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>，配置源重载后下一封信即用新值；
/// 新值同样经 <see cref="IValidateOptions{TOptions}"/> 校验，不合规时抛 <see cref="OptionsValidationException"/>。</para>
/// </remarks>
public sealed class SmtpEmailSender(
    IOptionsMonitor<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    // 测试换成调短 Timeout 的真实客户端，不必等满默认两分钟的读超时
    internal Func<SmtpClient> CreateClient { get; init; } = static () => new SmtpClient();

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var current = options.CurrentValue;
        var mime = BuildMessage(message, current);

        using var client = CreateClient();
        try
        {
            await client.ConnectAsync(current.Host, current.Port, ResolveSocketOptions(current), cancellationToken);
        }
        catch (Exception exception) when (IsUnreachable(exception))
        {
            throw new EmailDeliveryException(
                EmailDeliveryFailureKind.Unreachable,
                $"Could not connect to the SMTP server {current.Host}:{current.Port}.",
                exception);
        }

        if (!string.IsNullOrWhiteSpace(current.Username))
        {
            try
            {
                // CurrentValue 经 SmtpOptionsValidator 校验，只有用户名、没有口令的配置到不了这里。
                await client.AuthenticateAsync(current.Username, current.Password!, cancellationToken);
            }
            catch (Exception exception) when (IsAuthenticationRejected(exception))
            {
                // 用户名常是邮箱，不进消息。
                throw new EmailDeliveryException(
                    EmailDeliveryFailureKind.AuthenticationRejected,
                    $"The SMTP server {current.Host}:{current.Port} rejected the credentials or does not support authentication.",
                    exception);
            }
        }

        try
        {
            await client.SendAsync(mime, cancellationToken);
        }
        catch (ServiceNotAuthenticatedException exception)
        {
            // 服务器要求认证（530）而没有配置账号：管理员该查的同样是用户名与口令。
            throw new EmailDeliveryException(
                EmailDeliveryFailureKind.AuthenticationRejected,
                $"The SMTP server {current.Host}:{current.Port} requires authentication.",
                exception);
        }
        catch (Exception exception) when (IsDeliveryRejected(exception))
        {
            // 不带 SmtpCommandException.Mailbox：被拒的地址就是收件人或发件人。
            throw new EmailDeliveryException(
                EmailDeliveryFailureKind.DeliveryRejected,
                exception is SmtpCommandException command
                    ? $"The SMTP server {current.Host}:{current.Port} did not accept the message ({command.ErrorCode}, status {(int)command.StatusCode})."
                    : $"An address of the message is not a valid SMTPUTF8 address for the SMTP server {current.Host}:{current.Port}.",
                exception);
        }

        // 走到这里服务器已接受投递，QUIT 只是善后，它的失败不能把已投递的信报成失败。
        // MailKit 在 QUIT 阶段已吞掉取消、I/O 与协议异常，唯独网络读超时以 TimeoutException 抛出。
        try
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "SMTP QUIT timed out after the message was accepted; the send is still reported as successful");
        }

        // 收件人脱敏后再记（保留域名便于按域名聚合）；主题不脱敏，它是宿主给的文案，组件文档写明不要放个人数据。
        logger.LogInformation(
            "Email sent to {To} with subject {Subject}",
            TextRedactor.RedactEmail(message.To),
            message.Subject);
    }

    // 连接阶段：Socket 不通、TLS 握手失败、服务器不支持或拒绝 STARTTLS、问候被拒、连接中断与超时；参数错误与取消不在其列。
    // 连接阶段的 SmtpCommandException 只来自问候、EHLO 与 STARTTLS，都表示这台服务器此刻用不了。
    private static bool IsUnreachable(Exception exception)
        => exception is SocketException or SslHandshakeException or IOException or SmtpProtocolException
            or TimeoutException or NotSupportedException or SmtpCommandException;

    // 认证阶段：凭据被拒或服务器不支持认证。服务器回 4xx（如 454 暂时无法认证）是临时故障，凭据未必有错，原样抛出。
    private static bool IsAuthenticationRejected(Exception exception)
        => exception switch
        {
            AuthenticationException { InnerException: SmtpCommandException command } => (int)command.StatusCode is < 400 or >= 500,
            AuthenticationException or SaslException or NotSupportedException => true,
            _ => false,
        };

    // 发送阶段只有服务器明确拒收发件人、收件人或正文，以及地址过不了 SMTPUTF8 校验才算投递被拒；
    // 其他状态码、协议错误与超时原样抛出。
    private static bool IsDeliveryRejected(Exception exception)
        => exception is SmtpUtf8AddressException
            or SmtpCommandException
            {
                ErrorCode: SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.MessageNotAccepted
            };

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
