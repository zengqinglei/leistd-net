using System.Net.Sockets;
using Leistd.Email.Abstractions;
using Leistd.Email.Exceptions;
using Leistd.Email.Smtp;
using Leistd.Email.Smtp.Options;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Email.Tests.Smtp;

/// <summary>以服务器接受投递为界：之前的失败上抛，之后断开阶段的取消或超时不改写发送结果。</summary>
/// <remarks>经本机 SMTP 应答驱动真实 MailKit；调用方据异常回滚验证码挑战与发送配额，已投递的信被报成失败会让用户收到用不了的码。</remarks>
public sealed class SmtpEmailSenderProtocolTests
{
    private readonly FakeLogger<SmtpEmailSender> _logger = new();

    private SmtpEmailSender Sender(LoopbackSmtpServer server, int timeoutMilliseconds = 10_000, string? username = null)
        => Sender(server.Port, timeoutMilliseconds, username);

    private SmtpEmailSender Sender(int port, int timeoutMilliseconds = 10_000, string? username = null)
        => new(new FixedOptionsMonitor(new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = port,
            EnableSsl = false,
            Username = username,
            Password = username is null ? null : "wrong",
            DefaultFromAddress = "noreply@example.com",
        }), _logger)
        {
            CreateClient = () => new SmtpClient { Timeout = timeoutMilliseconds },
        };

    private static EmailMessage Message() => new()
    {
        To = "user@example.com",
        Subject = "Verification Code",
        Body = "<p>123456</p>",
    };

    [Fact]
    public async Task An_accepted_message_is_sent()
    {
        await using var server = new LoopbackSmtpServer();

        await Sender(server).SendAsync(Message());

        Assert.True(server.Accepted);
    }

    [Fact]
    public async Task Cancellation_during_quit_after_acceptance_still_reports_success()
    {
        using var request = new CancellationTokenSource();
        await using var server = new LoopbackSmtpServer(onQuit: request.Cancel, replyToQuit: false);

        await Sender(server).SendAsync(Message(), request.Token);

        Assert.True(server.Accepted);
        Assert.True(request.IsCancellationRequested);
    }

    [Fact]
    public async Task A_quit_timeout_after_acceptance_still_reports_success_and_is_logged()
    {
        await using var server = new LoopbackSmtpServer(replyToQuit: false);

        await Sender(server, timeoutMilliseconds: 200).SendAsync(Message());

        Assert.True(server.Accepted);
        var warning = Assert.Single(_logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.IsType<TimeoutException>(warning.Exception);
    }

    /// <summary>服务器明确拒收收件人或正文：归为投递被拒，调用方据此提示检查发件地址与收件人；原异常保留为内层。</summary>
    [Theory]
    [InlineData("550 5.1.1 no such user", "250 2.0.0 accepted", SmtpErrorCode.RecipientNotAccepted)]
    [InlineData("250 OK", "554 5.7.1 rejected", SmtpErrorCode.MessageNotAccepted)]
    public async Task A_rejected_message_throws_a_delivery_rejection(
        string recipientReply, string dataReply, SmtpErrorCode errorCode)
    {
        await using var server = new LoopbackSmtpServer(dataReply: dataReply, recipientReply: recipientReply);

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(server).SendAsync(Message()));

        Assert.Equal(EmailDeliveryFailureKind.DeliveryRejected, exception.FailureKind);
        Assert.Equal(errorCode, Assert.IsType<SmtpCommandException>(exception.InnerException).ErrorCode);
        Assert.DoesNotContain("user@example.com", exception.Message);
        Assert.False(server.Accepted);
    }

    /// <summary>
    /// 服务器拒绝凭据（535）或根本不支持认证：归为认证被拒，调用方据此提示检查用户名与口令。
    /// 用户名常是邮箱，不进异常消息。
    /// </summary>
    [Theory]
    [InlineData("535 5.7.8 authentication failed", typeof(MailKit.Security.AuthenticationException))]
    [InlineData(null, typeof(NotSupportedException))]
    public async Task Rejected_credentials_throw_an_authentication_rejection(string? authReply, Type innerType)
    {
        await using var server = new LoopbackSmtpServer(authReply: authReply);

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => Sender(server, username: "mailer@example.com").SendAsync(Message()));

        Assert.Equal(EmailDeliveryFailureKind.AuthenticationRejected, exception.FailureKind);
        Assert.IsType(innerType, exception.InnerException);
        Assert.DoesNotContain("mailer@example.com", exception.Message);
        Assert.False(server.Accepted);
    }

    /// <summary>端口上没有服务在听：归为连不上，调用方据此提示检查地址、端口与 TLS。</summary>
    [Fact]
    public async Task A_server_that_cannot_be_reached_throws_an_unreachable_failure()
    {
        int port;
        await using (var server = new LoopbackSmtpServer())
        {
            port = server.Port;
        }

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => Sender(port, timeoutMilliseconds: 2_000).SendAsync(Message()));

        Assert.Equal(EmailDeliveryFailureKind.Unreachable, exception.FailureKind);
        Assert.IsAssignableFrom<SocketException>(exception.InnerException);
    }

    [Fact]
    public async Task Cancellation_before_acceptance_throws()
    {
        using var request = new CancellationTokenSource();
        await using var server = new LoopbackSmtpServer(dataReply: null, onDataEnd: request.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(server).SendAsync(Message(), request.Token));
        Assert.False(server.Accepted);
    }

    // 发送阶段的超时归不进三类（信可能已在对方手里），原样抛出。
    [Fact]
    public async Task A_timeout_before_acceptance_throws()
    {
        await using var server = new LoopbackSmtpServer(dataReply: null);

        await Assert.ThrowsAsync<TimeoutException>(
            () => Sender(server, timeoutMilliseconds: 200).SendAsync(Message()));
        Assert.False(server.Accepted);
    }

    private sealed class FixedOptionsMonitor(SmtpOptions options) : IOptionsMonitor<SmtpOptions>
    {
        public SmtpOptions CurrentValue => options;

        public SmtpOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<SmtpOptions, string?> listener) => null;
    }
}
