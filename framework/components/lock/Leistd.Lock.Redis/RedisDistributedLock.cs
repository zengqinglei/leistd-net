using Leistd.Lock.Core;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Leistd.Lock.Redis;

/// <summary>
/// Redis 分布式锁实现
/// 使用 StackExchange.Redis 的 LockTake / LockRelease API（内部封装 SET NX + Lua 脚本）
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer connectionMultiplexer, ILogger<RedisDistributedLock> logger)
    : IDistributedLock
{
    private static readonly TimeSpan DefaultLockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public async Task<ILockHandle> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = await TryAcquireAsync(key, cancellationToken);
            if (handle != null)
                return handle;
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public async Task<ILockHandle?> TryLockAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = await TryAcquireAsync(key, cancellationToken);
            if (handle != null)
                return handle;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(PollInterval < remaining ? PollInterval : remaining, cancellationToken);
        }
        logger.LogDebug("尝试加锁【{Key}】失败：超时", key);
        return null;
    }

    private async Task<ILockHandle?> TryAcquireAsync(string key, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");

        var acquired = await TakeAsync(key, token, DefaultLockExpiry);
        if (!acquired)
            return null;

        logger.LogDebug("加锁【{Key}】成功", key);
        return new RedisLockHandle(
            key,
            DefaultLockExpiry,
            expiry => ExtendAsync(key, token, expiry),
            () => ReleaseAsync(key, token),
            logger);
    }

    /// <summary>
    /// 抢锁：SET NX + 过期时间。
    /// </summary>
    private async Task<bool> TakeAsync(string key, string token, TimeSpan expiry)
    {
        var db = connectionMultiplexer.GetDatabase();
        return await db.LockTakeAsync(key, token, expiry);
    }

    /// <summary>
    /// 续期：仅当 key 仍持有本次的 token 时才延长过期时间。
    /// </summary>
    /// <remarks>
    /// 必须校验 token：不校验就会在锁已经过期、被他人重新获取之后，把别人的锁续上，
    /// 那比不续期更糟——两个持有者都会认为自己独占。
    /// </remarks>
    private async Task<bool> ExtendAsync(string key, string token, TimeSpan expiry)
    {
        var db = connectionMultiplexer.GetDatabase();
        return await db.LockExtendAsync(key, token, expiry);
    }

    private async Task ReleaseAsync(string key, string token)
    {
        var db = connectionMultiplexer.GetDatabase();

        // 使用 LockRelease API（内部封装 Lua 脚本原子校验 + 删除）
        var released = await db.LockReleaseAsync(key, token);
        if (released)
            logger.LogDebug("解锁【{Key}】成功", key);
        else
            logger.LogWarning("解锁【{Key}】失败：锁已过期或被他人持有", key);
    }
}
