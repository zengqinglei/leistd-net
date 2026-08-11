using Leistd.Lock.Core;
using Microsoft.Extensions.Logging;

namespace Leistd.Lock.Redis;

/// <summary>
/// Redis 锁句柄：持有 key + token，后台按租约周期续期，释放时原子校验后删除。
/// </summary>
/// <remarks>
/// 不续期的租约只能保证"拿到锁的那一刻是独占的"。临界区一旦超过租约时长——数据库阻塞、
/// 网络抖动、批量初始化——锁会自动过期，另一个实例合法进入，而当前进程仍在写，两边都以为自己独占。
/// 因此这里持续续期；续期失败即通过 <see cref="LockLost"/> 通知持有者，让它中止而不是带着幻觉继续。
/// </remarks>
internal sealed class RedisLockHandle : ILockHandle
{
    private readonly string key;
    private readonly Func<TimeSpan, Task<bool>> extend;
    private readonly Func<Task> release;
    private readonly ILogger logger;
    private readonly CancellationTokenSource lockLost = new();
    private readonly CancellationTokenSource renewalStopped = new();
    private readonly Task renewalLoop;
    private int disposed;

    /// <remarks>
    /// 续期与释放以委托传入，而不是持有 <see cref="RedisDistributedLock"/>：
    /// 这两个动作是本类唯一需要的外部能力，收成委托后测试可以直接构造句柄验证续期与失锁行为，
    /// 不必为此把生产类型解封或公开扩展点——公共 API 不该为测试留口子。
    /// </remarks>
    public RedisLockHandle(
        string key,
        TimeSpan expiry,
        Func<TimeSpan, Task<bool>> extend,
        Func<Task> release,
        ILogger logger)
    {
        this.key = key;
        this.extend = extend;
        this.release = release;
        this.logger = logger;

        // 续期间隔取租约的三分之一：允许连续两次失败仍来得及在过期前放弃。
        var interval = TimeSpan.FromMilliseconds(Math.Max(expiry.TotalMilliseconds / 3, 1));
        renewalLoop = RenewAsync(interval, expiry);
    }

    public CancellationToken LockLost => lockLost.Token;

    public async ValueTask DisposeAsync()
    {
        // 与内存句柄保持同一契约：重复释放是无操作，而不是在已释放的 CTS 上抛异常。
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        await renewalStopped.CancelAsync();

        try
        {
            await renewalLoop;
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }

        renewalStopped.Dispose();

        try
        {
            // 已经失去锁时不再删除：那把锁现在属于别人，删掉等于把别人的临界区放开。
            if (!lockLost.IsCancellationRequested)
            {
                await release();
            }
        }
        finally
        {
            lockLost.Dispose();
        }
    }

    private async Task RenewAsync(TimeSpan interval, TimeSpan expiry)
    {
        try
        {
            while (!renewalStopped.IsCancellationRequested)
            {
                await Task.Delay(interval, renewalStopped.Token);

                if (!await extend(expiry))
                {
                    logger.LogWarning("续期锁【{Key}】失败，持锁资格已失效", key);
                    await lockLost.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 句柄被释放，正常退出。
        }
        catch (Exception exception)
        {
            // 续期通道本身出问题（连接断开等）同样意味着无法再证明自己持有锁。
            logger.LogWarning(exception, "续期锁【{Key}】异常，持锁资格视为失效", key);
            await lockLost.CancelAsync();
        }
    }
}
