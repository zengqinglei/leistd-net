#if (RemoteTokenAuth)
using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Infrastructure.Systems;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.UnitTests.Infrastructure;

public sealed class IdentitySystemDirectoryTests
{
    [Fact]
    public async Task Repeated_reads_share_the_short_lived_directory_cache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var api = new FixedApi();
        var directory = new IdentitySystemDirectory(api, provider.GetRequiredService<HybridCache>(), NullLogger<IdentitySystemDirectory>.Instance);
        Assert.Single(await directory.GetListAsync());
        Assert.Single(await directory.GetListAsync());
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task Nodes_share_the_directory_through_the_registered_distributed_cache()
    {
        var shared = new SharedDistributedCache();
        var api = new FixedApi();
        async Task<IdentitySystemDirectory> CreateNodeAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDistributedCache>(shared);
            services.AddHybridCache();
            var provider = services.BuildServiceProvider();
            providers.Add(provider);
            return new IdentitySystemDirectory(api, provider.GetRequiredService<HybridCache>(), NullLogger<IdentitySystemDirectory>.Instance);
        }

        Assert.Single(await (await CreateNodeAsync()).GetListAsync());
        Assert.Equal("identity", Assert.Single(await (await CreateNodeAsync()).GetListAsync()).Key);
        Assert.Equal(1, api.Calls);
        foreach (var provider in providers) await provider.DisposeAsync();
    }

    private readonly List<ServiceProvider> providers = [];

    // 包一层，避免被识别为进程内的 MemoryDistributedCache。
    private sealed class SharedDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        public byte[]? Get(string key) => inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);
        public void Refresh(string key) => inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
        public void Remove(string key) => inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            inner.SetAsync(key, value, options, token);
    }

    private sealed class FixedApi : IIdentitySystemsApi
    {
        public int Calls { get; private set; }
        public Task<HttpResponseMessage> GetListAsync(string culture, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new { key = "identity", displayName = "Identity", homeUrl = "https://identity.test/", isCurrent = true } })
            });
        }
    }
}
#endif
