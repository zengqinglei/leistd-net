using Leistd.Email.Abstractions;
using Leistd.Email.Smtp;
using Leistd.Email.Smtp.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Email.Tests.Smtp;

/// <summary>
/// 发送失败必须上抛。
/// </summary>
/// <remarks>
/// 这是本组件存在的直接动因：上一版实现在 <c>Host</c> 为空或等于默认示例值时静默返回，
/// 调用方以为信已发出。而 <c>EmailVerificationAppService</c> 正是靠这次异常
/// 回滚已占用的限流槽位与挑战缓存——静默成功会把一个永远收不到码的挑战交给用户。
/// </remarks>
public sealed class SmtpEmailSenderTests
{
    // 保留给 IANA 的丢弃端口，本机上不会有服务监听
    private const int UnreachablePort = 9;

    private static SmtpEmailSender Sender(Action<SmtpOptions>? configure = null)
    {
        var options = new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = UnreachablePort,
            EnableSsl = false,
            DefaultFromAddress = "noreply@example.com",
        };
        configure?.Invoke(options);

        return new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<SmtpEmailSender>.Instance);
    }

    private static EmailMessage Message() => new()
    {
        To = "user@example.com",
        Subject = "Account Registration Verification Code",
        Body = "<p>123456</p>",
    };

    [Fact]
    public async Task An_unreachable_host_throws_instead_of_reporting_success()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<Exception>(() => Sender().SendAsync(Message(), cts.Token));
    }

    // 空 Host 曾是"静默跳过发送"的触发条件之一，现在必须与其它连接失败一样抛
    [Fact]
    public async Task A_blank_host_throws_rather_than_skipping_the_send()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<Exception>(
            () => Sender(o => o.Host = string.Empty).SendAsync(Message(), cts.Token));
    }

    // 曾经的哨兵值：配置像默认示例就假装发出去。现在它只是一个连不通的主机名
    [Fact]
    public async Task The_former_placeholder_host_is_no_longer_treated_as_a_skip_signal()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await Assert.ThrowsAnyAsync<Exception>(
            () => Sender(o => o.Host = "smtp.example.com").SendAsync(Message(), cts.Token));
    }

    [Fact]
    public async Task A_null_message_is_rejected()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => Sender().SendAsync(null!));
}
