using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Leistd.Ddd.Infrastructure.Tests;

/// <summary>替换宿主时间源后，进程内缓存的到期行为随之切换，宿主显式配置的时钟优先。</summary>
public sealed class CacheClockRegistrationTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    [Fact]
    public void Memory_cache_entries_expire_when_the_host_clock_advances()
    {
        var services = new ServiceCollection().AddMemoryCache();
        var time = RegisterClock(services);
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IMemoryCache>();

        cache.Set("key", "cached", Lifetime);
        Assert.Equal("cached", cache.Get<string>("key"));
        time.Advance(Lifetime + TimeSpan.FromSeconds(1));
        Assert.Null(cache.Get<string>("key"));
    }

    [Fact]
    public async Task Hybrid_cache_local_entries_expire_when_the_host_clock_advances()
    {
        var services = new ServiceCollection();
        var time = RegisterClock(services);
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        await cache.SetAsync("key", "cached", new HybridCacheEntryOptions { LocalCacheExpiration = Lifetime });
        Assert.Equal("cached", await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult("fresh")));
        time.Advance(Lifetime + TimeSpan.FromSeconds(1));
        Assert.Equal("fresh", await cache.GetOrCreateAsync("key", _ => ValueTask.FromResult("fresh")));
    }

    [Fact]
    public void Distributed_memory_cache_entries_expire_when_the_host_clock_advances()
    {
        var services = new ServiceCollection().AddDistributedMemoryCache();
        var time = RegisterClock(services);
        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();

        cache.SetString("key", "cached", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
        Assert.Equal("cached", cache.GetString("key"));
        time.Advance(Lifetime + TimeSpan.FromSeconds(1));
        Assert.Null(cache.GetString("key"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_caches_keep_explicit_clocks_registered_by_the_host(bool configureFirst)
    {
        var services = new ServiceCollection();
        var hostClock = new HostCacheClock();
        if (configureFirst) ConfigureCaches();
        var time = RegisterClock(services);
        if (!configureFirst) ConfigureCaches();
        using var provider = services.BuildServiceProvider();
        var memory = provider.GetRequiredService<IMemoryCache>();
        var distributed = provider.GetRequiredService<IDistributedCache>();

        memory.Set("key", "cached", Lifetime);
        distributed.SetString("key", "cached", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
        time.Advance(Lifetime + TimeSpan.FromSeconds(1));
        Assert.Equal("cached", memory.Get<string>("key"));
        Assert.Equal("cached", distributed.GetString("key"));

        void ConfigureCaches()
        {
            services.AddMemoryCache(options => options.Clock = hostClock);
            services.AddDistributedMemoryCache(options => options.Clock = hostClock);
        }
    }

    private static FakeTimeProvider RegisterClock(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDddInfrastructure();
        var time = new FakeTimeProvider();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(time);
        return time;
    }

    private sealed class HostCacheClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}
