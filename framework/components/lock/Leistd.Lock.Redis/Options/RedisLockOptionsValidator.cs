using Microsoft.Extensions.Options;

namespace Leistd.Lock.Redis.Options;

// 启动期校验锁有效期与重试间隔为正，避免锁立即失效或忙轮询；键前缀不作格式限制。
internal sealed class RedisLockOptionsValidator : IValidateOptions<RedisLockOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RedisLockOptions options)
    {
        var failures = new List<string>();

        if (options.Expiry <= TimeSpan.Zero)
        {
            failures.Add(
                $"Expiry must be greater than zero (was {options.Expiry}). It is the lock lease; " +
                "a non-positive value makes the lock expire immediately or be rejected by Redis, " +
                "so mutual exclusion silently stops holding.");
        }

        if (options.RetryInterval <= TimeSpan.Zero)
        {
            failures.Add(
                $"RetryInterval must be greater than zero (was {options.RetryInterval}). " +
                "A non-positive value turns lock acquisition into a busy loop against Redis.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{RedisLockOptions.SectionName}: {string.Join(" ", failures)}");
    }
}
