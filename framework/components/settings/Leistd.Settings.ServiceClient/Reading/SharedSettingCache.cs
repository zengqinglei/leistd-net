using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Http;
using Leistd.Settings.Dtos;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Leistd.Settings.ServiceClient.Reading;

// 按（提供方，租户）缓存整份共享值。结构与多租户的 TenantRouteCache 相同：HybridCache 只用进程内一级、负责同键单飞；
// 有旧值时单次回源最多等 1 秒，超时或暂不可达先用旧值并节流 30 秒，每个节流周期一条 Warning。
// 与它不同的两点：回源带上一次的 ETag；没有可用旧值或明确失败时不抛出，交回 null，由读取器按每项兜底值返回。
// 单例：回源用根容器解析客户端，可能在调用方的请求结束后继续，不能借用调用方作用域里的实例
internal sealed class SharedSettingCache(HybridCache cache, TimeProvider clock, IServiceProvider services, ILogger<SharedSettingCache> logger)
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly HybridCacheEntryOptions InFlightOptions = new()
    {
        Flags = HybridCacheEntryFlags.DisableDistributedCache | HybridCacheEntryFlags.DisableLocalCacheWrite
    };

    // 读一个提供方在一个租户下的整份共享值；null 表示读不到
    public async ValueTask<CachedValues?> GetAsync(SharedSettingProviderRegistration provider, string tenantSegment,
        CancellationToken cancellationToken)
    {
        var options = provider.GetOptions(services);
        var key = $"leistd:shared-settings:{provider.Name}:{tenantSegment}";
        var entry = await ReadAsync(key, cancellationToken);
        if (entry is not null && clock.GetUtcNow() < entry.RetryAt) return Use(entry, options.FreshFor);
        var pending = cache.GetOrCreateAsync<Entry?>(key + ":fetch", async ct =>
        {
            var current = await ReadAsync(key, ct);
            if (current is not null && clock.GetUtcNow() < current.RetryAt) return current;
            return await FetchAsync(key, provider, tenantSegment, options.FreshFor, options.MaxStaleness, current, ct);
        }, InFlightOptions, cancellationToken: cancellationToken).AsTask();
        if (entry is null) return Use(await pending, options.FreshFor);
        try
        {
            return Use(await pending.WaitAsync(WaitBudget, clock, CancellationToken.None), options.FreshFor);
        }
        catch (TimeoutException)
        {
            // 回源自身的结果照常交回；只有等待预算耗尽才先用旧值，原回源继续并写回缓存
            if (pending.IsCompleted) return Use(await pending, options.FreshFor);
            _ = pending.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            cancellationToken.ThrowIfCancellationRequested();
            var previous = await UseStaleAsync(key, provider.Name, options.FreshFor, true, cancellationToken);
            return Use(previous ?? await pending, options.FreshFor);
        }
    }

    private async Task<Entry?> FetchAsync(string key, SharedSettingProviderRegistration provider,
        string tenantSegment, TimeSpan freshFor, TimeSpan maxStaleness, Entry? current, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await provider.Fetch(services, tenantSegment, current?.ETag, cancellationToken);
            var now = clock.GetUtcNow();
            if (response.StatusCode == HttpStatusCode.NotModified && current is not null)
            {
                return await StoreAsync(key, current with { ConfirmedAt = now, RetryAt = now + freshFor, HardDeadline = now + freshFor + maxStaleness },
                    cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await response.CreateRemoteErrorAsync(cancellationToken);
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var values = (await JsonSerializer.DeserializeAsync<SharedSettingsDto>(body, SerializerOptions, cancellationToken))?.Values
                         ?? throw new ServiceClientException($"Shared settings provider '{provider.Name}' returned an empty representation.",
                             failureKind: ServiceClientFailureKind.InvalidResponse);
            var missing = provider.Fallbacks.Keys.Where(name => !values.ContainsKey(name)).ToList();
            if (missing.Count > 0)
            {
                logger.LogWarning("Shared settings provider {Provider} does not share {Settings}; their fallbacks are used.",
                    provider.Name, string.Join(", ", missing));
            }

            return await StoreAsync(key, new Entry(new Dictionary<string, string?>(values, StringComparer.Ordinal),
                response.Headers.ETag?.ToString(), now, now + freshFor, now + freshFor + maxStaleness), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            var previous = await UseStaleAsync(key, provider.Name, freshFor, false, cancellationToken);
            if (previous is null)
            {
                logger.LogWarning(exception, "Shared settings provider {Provider} is unavailable and no earlier values are cached for {Tenant}; fallbacks are used.",
                    provider.Name, tenantSegment);
            }

            return previous;
        }
        catch (Exception exception)
        {
            // 明确失败（身份被拒、租户不存在、响应无效）：旧值不能再用，之后再遇暂时故障也不恢复
            await cache.RemoveAsync(key, cancellationToken);
            if (exception is RemoteServiceException { RemoteStatusCode: 404 })
            {
                logger.LogWarning("Shared settings provider {Provider} has no tenant {Tenant}; fallbacks are used.", provider.Name, tenantSegment);
            }
            else
            {
                logger.LogError(exception, "Shared settings provider {Provider} rejected the read for {Tenant}; earlier values are dropped and fallbacks are used.",
                    provider.Name, tenantSegment);
            }

            return null;
        }
    }

    // 传输不可达、超时、408/429/5xx；HttpClient 自身的兜底超时以取消异常出现，调用方未取消时同属超时
    private static bool IsUnavailable(Exception exception) => exception switch
    {
        ServiceClientException serviceClient => ServiceClientFailureClassifier.IsUnavailable(serviceClient),
        OperationCanceledException => true,
        _ => false
    };

    private async Task<Entry> StoreAsync(string key, Entry entry, CancellationToken cancellationToken)
    {
        var ttl = entry.HardDeadline - clock.GetUtcNow();
        if (ttl > TimeSpan.Zero)
        {
            await cache.SetAsync(key, entry, new HybridCacheEntryOptions
            {
                Flags = HybridCacheEntryFlags.DisableDistributedCache,
                Expiration = ttl, LocalCacheExpiration = ttl
            }, cancellationToken: cancellationToken);
        }

        return entry;
    }

    // 共享节流转换，避免同键的多个超时等待者各自覆盖 RetryAt、重复记日志；不缓存这个协调结果
    private ValueTask<Entry?> UseStaleAsync(string key, string provider, TimeSpan freshFor, bool timedOut, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync<Entry?>(key + ":stale", async ct =>
        {
            var previous = await ReadAsync(key, ct);
            var now = clock.GetUtcNow();
            if (previous is null || now >= previous.HardDeadline) return null;
            if (timedOut && now < previous.RetryAt) return previous;
            var retry = now + RetryDelay;
            var result = previous with { RetryAt = retry < previous.HardDeadline ? retry : previous.HardDeadline };
            await StoreAsync(key, result, ct);
            if (now >= previous.RetryAt)
                logger.LogWarning("Shared settings provider {Provider} exceeded its wait budget or is unavailable; using values stale for {Staleness} while validation may continue.",
                    provider, now - (previous.ConfirmedAt + freshFor));
            return result;
        }, InFlightOptions, cancellationToken: cancellationToken);

    private ValueTask<Entry?> ReadAsync(string key, CancellationToken ct) =>
        cache.GetOrCreateAsync<Entry?>(key, static _ => ValueTask.FromResult<Entry?>(null), new HybridCacheEntryOptions
        {
            Flags = HybridCacheEntryFlags.DisableDistributedCache | HybridCacheEntryFlags.DisableLocalCacheWrite |
                HybridCacheEntryFlags.DisableUnderlyingData
        }, cancellationToken: ct);

    private CachedValues? Use(Entry? entry, TimeSpan freshFor) =>
        entry is null ? null : new CachedValues(entry.Values, clock.GetUtcNow() >= entry.ConfirmedAt + freshFor);

    [ImmutableObject(true)]
    private sealed record Entry(IReadOnlyDictionary<string, string?> Values, string? ETag, DateTimeOffset ConfirmedAt,
        DateTimeOffset RetryAt, DateTimeOffset HardDeadline);
}

// 一次读取拿到的整份值；Stale 表示新鲜期已过、用的是保留期内的旧值
internal sealed record CachedValues(IReadOnlyDictionary<string, string?> Values, bool Stale);
