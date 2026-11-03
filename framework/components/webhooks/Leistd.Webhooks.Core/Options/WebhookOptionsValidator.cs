using System.Net;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.Options;

internal sealed class WebhookOptionsValidator(string configSectionPath) : IValidateOptions<WebhookOptions>
{
    // 退避表过长时自动重试会拖到数周之后，多半是配置写错。
    internal const int MaxRetryCount = 20;

    // 选项绑定的配置节；重复注册时据此拒绝另一路径。
    public string ConfigSectionPath { get; } = configSectionPath;

    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        var failures = new List<string>();
        var prefix = ConfigSectionPath + ":";

        if (options.RetrySchedule is { } schedule)
        {
            if (schedule.Count > MaxRetryCount)
            {
                failures.Add($"{prefix}RetrySchedule must not contain more than {MaxRetryCount} items.");
            }

            for (var i = 0; i < schedule.Count; i++)
            {
                if (schedule[i] <= TimeSpan.Zero || schedule[i] > TimeSpan.FromDays(7))
                {
                    failures.Add($"{prefix}RetrySchedule:{i} must be greater than 0 and at most 7 days.");
                }
            }
        }

        if (options.RequestTimeout < TimeSpan.FromSeconds(1) || options.RequestTimeout > TimeSpan.FromMinutes(5))
        {
            failures.Add($"{prefix}RequestTimeout must be between 1 second and 5 minutes.");
        }

        if (options.DispatchInterval < TimeSpan.FromSeconds(1) || options.DispatchInterval > TimeSpan.FromHours(1))
        {
            failures.Add($"{prefix}DispatchInterval must be between 1 second and 1 hour.");
        }

        if (options.AutoDisableAfter <= TimeSpan.Zero)
        {
            failures.Add($"{prefix}AutoDisableAfter must be greater than 0.");
        }

        if (options.SecretRotationOverlap < TimeSpan.Zero || options.SecretRotationOverlap > TimeSpan.FromDays(30))
        {
            failures.Add($"{prefix}SecretRotationOverlap must be between 0 and 30 days.");
        }

        for (var i = 0; i < options.AllowedNetworks.Count; i++)
        {
            if (!IPNetwork.TryParse(options.AllowedNetworks[i], out _))
            {
                failures.Add($"{prefix}AllowedNetworks:{i} must be a CIDR network such as 10.0.0.0/8.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
