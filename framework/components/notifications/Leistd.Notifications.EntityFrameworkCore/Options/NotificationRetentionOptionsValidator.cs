using Microsoft.Extensions.Options;

namespace Leistd.Notifications.EntityFrameworkCore.Options;

internal sealed class NotificationRetentionOptionsValidator : IValidateOptions<NotificationRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationRetentionOptions options)
    {
        var failures = new List<string>();
        if (options.ReadRetentionDays is < 1 or > 3650)
        {
            failures.Add($"{NotificationRetentionOptions.SectionName}:ReadRetentionDays must be between 1 and 3650.");
        }

        if (options.UnreadRetentionDays is < 1 or > 3650)
        {
            failures.Add($"{NotificationRetentionOptions.SectionName}:UnreadRetentionDays must be between 1 and 3650.");
        }

        // 未读比已读先删就本末倒置：用户还没看到的通知反而先消失
        if (options.UnreadRetentionDays < options.ReadRetentionDays)
        {
            failures.Add($"{NotificationRetentionOptions.SectionName}:UnreadRetentionDays must not be shorter than ReadRetentionDays.");
        }

        if (options.DailyRunHourUtc is < 0 or > 23)
        {
            failures.Add($"{NotificationRetentionOptions.SectionName}:DailyRunHourUtc must be between 0 and 23.");
        }

        if (options.BatchSize is < 100 or > 10000)
        {
            failures.Add($"{NotificationRetentionOptions.SectionName}:BatchSize must be between 100 and 10000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
