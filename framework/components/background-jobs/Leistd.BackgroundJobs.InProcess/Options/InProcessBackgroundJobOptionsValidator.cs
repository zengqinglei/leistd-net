using Microsoft.Extensions.Options;

namespace Leistd.BackgroundJobs.InProcess.Options;

// 容量越界在启动期拒绝，不在建队列时静默改写：被改写后队列只剩 1 格，
// 表现是入队方一直在等，而配置看上去是 256。
internal sealed class InProcessBackgroundJobOptionsValidator : IValidateOptions<InProcessBackgroundJobOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, InProcessBackgroundJobOptions options)
        => options.QueueCapacity >= 1
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{InProcessBackgroundJobOptions.SectionName}:QueueCapacity must be at least 1 " +
                $"(was {options.QueueCapacity}).");
}
