using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.EntityFrameworkCore.Options;

internal sealed class WebhookRetentionOptionsValidator(string configSectionPath) : IValidateOptions<WebhookRetentionOptions>
{
    // 选项绑定的配置节；重复注册时据此拒绝另一路径。
    public string ConfigSectionPath { get; } = configSectionPath;

    public ValidateOptionsResult Validate(string? name, WebhookRetentionOptions options)
    {
        var failures = new List<string>();
        if (options.Days is < 1 or > 3650)
        {
            failures.Add($"{ConfigSectionPath}:Days must be between 1 and 3650.");
        }

        if (options.DailyRunHourUtc is < 0 or > 23)
        {
            failures.Add($"{ConfigSectionPath}:DailyRunHourUtc must be between 0 and 23.");
        }

        if (options.BatchSize is < 100 or > 10000)
        {
            failures.Add($"{ConfigSectionPath}:BatchSize must be between 100 and 10000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
