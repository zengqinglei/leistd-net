using Microsoft.Extensions.Options;

namespace Leistd.Lock.Redis.Options;

// 启动期校验 RedisLockOptions，非法取值直接阻止宿主启动
// 两个时间值配错都是静默的生产事故：
// <list type="bullet">
// RedisLockOptions.RetryInterval 为零或负数会让抢锁退化成对 Redis 的忙轮询——
// 表现是 Redis CPU 无故打满、而应用日志一切正常；
// RedisLockOptions.Expiry 为零或负数会让 SET NX PX 拿到一个非正过期时间，
// 锁要么立即失效（互斥当场不成立）要么被 provider 拒绝，两种都不会在配置阶段暴露。
// 只校验这两条。前缀格式不校验——那是命名约定，不是正确性边界，为它阻断启动是过度。
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
