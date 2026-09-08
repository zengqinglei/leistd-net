using MimeKit;
using Microsoft.Extensions.Options;

namespace Leistd.Email.Smtp.Options;

// 启动期校验 SmtpOptions，非法取值直接阻止宿主启动。
// 这四条的共同点是：留到运行期才发现的话，代价是"用户已经点了发送"——
// 注册验证码这类流程会因此把一个永远收不到码的挑战交给用户。
// 只校验能在启动期机械判定的边界；连通性与认证不在此校验（那要真连一次，
// 且失败会以异常形式在发送时暴露，不是静默错误）。
internal sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("Host is required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"Port must be between 1 and 65535 (was {options.Port}).");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultFromAddress))
        {
            failures.Add("DefaultFromAddress is required; it is the sender identity used when a message does not set one.");
        }
        else if (!IsAddrSpec(options.DefaultFromAddress))
        {
            failures.Add(
                $"DefaultFromAddress '{options.DefaultFromAddress}' is not a bare mailbox address. " +
                "Expected just 'user@host'; if you meant to set a display name, use DefaultFromName.");
        }

        var hasUser = !string.IsNullOrWhiteSpace(options.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(options.Password);
        if (hasUser != hasPassword)
        {
            failures.Add(
                "Username and Password must be set together or both left empty. " +
                "Setting only one makes the sender skip authentication while looking configured, " +
                "so the server either relays anonymously or refuses the message.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{SmtpOptions.SectionName}: {string.Join(" ", failures)}");
    }

    // 用发送路径同一个构造来判定，两边就不可能漂移。
    // MailboxAddress.TryParse 不能用：它接受 "Name <user@host>" 这种完整形态，而
    // MailboxAddress(name, address) 的地址参数只接受 addr-spec——那类取值能过启动校验，
    // 却让每一封走默认发件人的信在构造阶段抛 ParseException，启动校验的意义正好被绕开。
    private static bool IsAddrSpec(string value)
    {
        try
        {
            _ = new MailboxAddress(string.Empty, value);
            return true;
        }
        catch (ParseException)
        {
            return false;
        }
    }
}
