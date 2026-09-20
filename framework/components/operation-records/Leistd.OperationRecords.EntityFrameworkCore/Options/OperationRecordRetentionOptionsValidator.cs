using Microsoft.Extensions.Options;

namespace Leistd.OperationRecords.EntityFrameworkCore.Options;

// 区间校验放在选项上：设置面板写进来的越界值在这里被拒，整组不生效，归档照旧按上一组合规值运行
internal sealed class OperationRecordRetentionOptionsValidator : IValidateOptions<OperationRecordRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, OperationRecordRetentionOptions options)
    {
        var failures = new List<string>();
        if (options.RetentionDays is < OperationRecordRetentionOptions.MinimumRetentionDays or > OperationRecordRetentionOptions.MaximumRetentionDays)
        {
            failures.Add($"{OperationRecordRetentionOptions.SectionName}:RetentionDays must be between {OperationRecordRetentionOptions.MinimumRetentionDays} and {OperationRecordRetentionOptions.MaximumRetentionDays}.");
        }

        if (options.DailyRunHourUtc is < 0 or > 23)
        {
            failures.Add($"{OperationRecordRetentionOptions.SectionName}:DailyRunHourUtc must be between 0 and 23.");
        }

        if (options.BatchSize is < 100 or > 10000)
        {
            failures.Add($"{OperationRecordRetentionOptions.SectionName}:BatchSize must be between 100 and 10000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
