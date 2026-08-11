using Leistd.Lock.Core;
using Leistd.Lock.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Leistd.Lock.Tests;

/// <summary>
/// Redis 锁句柄的续期与失锁行为。
/// </summary>
/// <remarks>
/// 不续期的租约只保证"拿到锁的那一刻是独占的"：临界区一旦超过租约时长，锁自动过期，
/// 另一个实例合法进入，而当前进程仍在写。这条路径没有测试就等于没有。
///
/// 直接构造句柄并注入续期/释放委托，而不是给 StackExchange.Redis 的 <c>IDatabase</c> 造 mock
/// （两百余个成员），也不为测试解封生产类型——公共 API 不该为测试留扩展点。
/// </remarks>
public class RedisLockRenewalTests
{
    private static readonly TimeSpan ShortExpiry = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task The_lease_is_renewed_while_the_handle_is_held()
    {
        var probe = new HandleProbe();

        await using (probe.CreateHandle())
        {
            // 持锁时间明显超过租约：不续期的实现到这里锁早已过期。
            await Task.Delay(ShortExpiry * 4);
            Assert.True(probe.ExtendCount >= 2);
        }

        Assert.Equal(1, probe.ReleaseCount);
    }

    [Fact]
    public async Task Losing_the_lease_cancels_the_handle_and_skips_release()
    {
        var probe = new HandleProbe { ExtendResult = false };

        var handle = probe.CreateHandle();

        // 续期返回 false 意味着 key 已不再持有本次的 token——锁现在属于别人。
        await WaitForLockLostAsync(handle);
        await handle.DisposeAsync();

        // 此时删除 key 等于把新持有者的临界区放开，必须跳过。
        Assert.Equal(0, probe.ReleaseCount);
    }

    [Fact]
    public async Task A_failing_renewal_channel_also_counts_as_losing_the_lease()
    {
        var probe = new HandleProbe { ExtendThrows = true };

        var handle = probe.CreateHandle();

        // 连接断开时无法再证明自己持有锁，与续期失败同等对待。
        await WaitForLockLostAsync(handle);
        await handle.DisposeAsync();

        Assert.Equal(0, probe.ReleaseCount);
    }

    [Fact]
    public async Task Disposing_stops_the_renewal_loop()
    {
        var probe = new HandleProbe();

        await using (probe.CreateHandle())
        {
            await Task.Delay(ShortExpiry);
        }

        var afterDispose = probe.ExtendCount;
        await Task.Delay(ShortExpiry * 3);

        // 释放之后仍在续期，等于持续给一把已经不属于自己的锁续命。
        Assert.Equal(afterDispose, probe.ExtendCount);
    }

    [Fact]
    public async Task Disposing_twice_is_a_no_op()
    {
        var probe = new HandleProbe();
        var handle = probe.CreateHandle();

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        // 与内存句柄同一契约：重复释放既不抛异常，也不重复删 key。
        Assert.Equal(1, probe.ReleaseCount);
    }

    [Fact]
    public async Task Concurrent_disposal_releases_once()
    {
        var probe = new HandleProbe();
        var handle = probe.CreateHandle();

        await Task.WhenAll(
            Task.Run(async () => await handle.DisposeAsync()),
            Task.Run(async () => await handle.DisposeAsync()));

        Assert.Equal(1, probe.ReleaseCount);
    }

    /// <summary>
    /// 有界等待失锁信号。
    /// </summary>
    /// <remarks>
    /// 不设上限的话，一旦续期逻辑被改坏，这些用例会挂住而不是失败——
    /// 挂起的测试比失败的测试更难排查，CI 上还会拖到超时才收场。
    /// </remarks>
    private static async Task WaitForLockLostAsync(ILockHandle handle)
    {
        var deadline = DateTime.UtcNow + ShortExpiry * 20;

        while (!handle.LockLost.IsCancellationRequested)
        {
            Assert.True(DateTime.UtcNow < deadline, "持锁资格失效后 LockLost 应当被取消。");
            await Task.Delay(20);
        }
    }

    private sealed class HandleProbe
    {
        private int extendCount;
        private int releaseCount;

        public bool ExtendResult { get; init; } = true;

        public bool ExtendThrows { get; init; }

        public int ExtendCount => Volatile.Read(ref extendCount);

        public int ReleaseCount => Volatile.Read(ref releaseCount);

        public ILockHandle CreateHandle()
            => new RedisLockHandle("k", ShortExpiry, ExtendAsync, ReleaseAsync, NullLogger.Instance);

        private Task<bool> ExtendAsync(TimeSpan expiry)
        {
            Interlocked.Increment(ref extendCount);

            return ExtendThrows
                ? Task.FromException<bool>(new RedisConnectionException(ConnectionFailureType.SocketFailure, "boom"))
                : Task.FromResult(ExtendResult);
        }

        private Task ReleaseAsync()
        {
            Interlocked.Increment(ref releaseCount);
            return Task.CompletedTask;
        }
    }
}
