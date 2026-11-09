using Microsoft.Extensions.Options;

namespace Leistd.Settings.ServiceClient.Options;

// 校验消息按实际配置节报键名
internal sealed class SharedSettingsClientOptionsValidator<TOptions>(string configSectionPath) : IValidateOptions<TOptions>
    where TOptions : SharedSettingsClientOptions
{
    public string ConfigSectionPath { get; } = configSectionPath;

    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            failures.Add($"{ConfigSectionPath}:{nameof(options.Path)} is required.");
        }

        if (options.FreshFor <= TimeSpan.Zero)
        {
            failures.Add($"{ConfigSectionPath}:{nameof(options.FreshFor)} must be greater than zero.");
        }

        if (options.MaxStaleness < TimeSpan.Zero)
        {
            failures.Add($"{ConfigSectionPath}:{nameof(options.MaxStaleness)} must not be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
