using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Leistd.MultiTenancy.ConnectionStrings;

internal sealed class TenantRouteCache(IMemoryCache cache, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, Task> inFlight = new();

    public async ValueTask<T> GetAsync<T>(string key, TimeSpan freshFor, TimeSpan maxStaleness,
        Func<CancellationToken, ValueTask<T>> fetch, Func<Exception, bool> unavailable, CancellationToken cancellationToken,
        ILogger logger)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = cache.Get<Entry<T>>(key);
        if (entry is not null && clock.GetUtcNow() < entry.RetryAt) return entry.Value;

        TaskCompletionSource<T>? started = null;
        Task<T> pending;
        lock (inFlight)
        {
            // 首次读取与登记任务之间，已有回源可能刚写入结果。
            entry = cache.Get<Entry<T>>(key);
            if (entry is not null && clock.GetUtcNow() < entry.RetryAt) return entry.Value;
            if (inFlight.TryGetValue(key, out var shared))
                pending = (Task<T>)shared;
            else
            {
                started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                inFlight[key] = pending = started.Task;
            }
        }
        if (started is not null) _ = CompleteFetchAsync(started);

        if (entry is null) return await pending.WaitAsync(cancellationToken);
        try
        {
            return await pending.WaitAsync(WaitBudget, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            // 回源自身的超时仍按原结果传播。
            if (pending.IsCompleted) return await pending;
            cancellationToken.ThrowIfCancellationRequested();
            var previous = UseStale<T>(key, freshFor, timedOut: true, logger);
            return previous is null ? await pending.WaitAsync(cancellationToken) : previous.Value;
        }

        async Task CompleteFetchAsync(TaskCompletionSource<T> completion)
        {
            try
            {
                completion.SetResult(await RefreshAsync(key, freshFor, maxStaleness, fetch, unavailable, logger));
            }
            catch (OperationCanceledException exception)
            {
                completion.SetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
                // 调用方可能都已结束等待。
                _ = completion.Task.Exception;
            }
        }
    }

    private async Task<T> RefreshAsync<T>(string key, TimeSpan freshFor, TimeSpan maxStaleness,
        Func<CancellationToken, ValueTask<T>> fetch, Func<Exception, bool> unavailable, ILogger logger)
    {
        try
        {
            Entry<T> result;
            try
            {
                var value = await fetch(CancellationToken.None);
                var now = clock.GetUtcNow();
                result = new Entry<T>(value, now, now + freshFor, now + freshFor + maxStaleness);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (!unavailable(exception))
                {
                    lock (inFlight) cache.Remove(key);
                    throw;
                }

                var previous = UseStale<T>(key, freshFor, timedOut: false, logger);
                if (previous is null) throw;
                return previous.Value;
            }

            lock (inFlight)
            {
                var ttl = result.HardDeadline - clock.GetUtcNow();
                if (ttl > TimeSpan.Zero) cache.Set(key, result, ttl);
            }
            return result.Value;
        }
        finally
        {
            lock (inFlight) inFlight.Remove(key);
        }
    }

    // 等待超时与后台更新可交错，必须在同一锁内读取并设置节流。
    private Entry<T>? UseStale<T>(string key, TimeSpan freshFor, bool timedOut, ILogger logger)
    {
        lock (inFlight)
        {
            var previous = cache.Get<Entry<T>>(key);
            var now = clock.GetUtcNow();
            if (previous is null || now >= previous.HardDeadline) return null;
            if (timedOut && now < previous.RetryAt) return previous;
            var retry = now + RetryDelay;
            var result = previous with { RetryAt = retry < previous.HardDeadline ? retry : previous.HardDeadline };
            cache.Set(key, result, result.HardDeadline - now);
            if (now >= previous.RetryAt)
                logger.LogWarning("Control-plane dependency {Dependency} exceeded its wait budget or is unavailable; using state stale for {Staleness} while validation may continue.",
                    key, now - (previous.ConfirmedAt + freshFor));
            return result;
        }
    }

    private sealed record Entry<T>(T Value, DateTimeOffset ConfirmedAt, DateTimeOffset RetryAt, DateTimeOffset HardDeadline);
}
