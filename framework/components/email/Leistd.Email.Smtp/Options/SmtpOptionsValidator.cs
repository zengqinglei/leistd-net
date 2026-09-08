using MimeKit;
using Microsoft.Extensions.Options;

namespace Leistd.Email.Smtp.Options;

// 只校验本地配置；连通性与认证由发送路径验证。
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

    // 与发送路径共用 addr-spec 构造规则；TryParse 还接受带显示名的完整邮箱。
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
