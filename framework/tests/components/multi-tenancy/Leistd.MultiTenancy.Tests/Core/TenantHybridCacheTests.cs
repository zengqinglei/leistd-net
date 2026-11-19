using System.Reflection;
using Leistd.MultiTenancy.Caching;
using Leistd.MultiTenancy.Context;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests.Core;

public sealed class TenantHybridCacheTests
{
    [Fact]
    public async Task Direct_cache_factory_with_a_cancelable_token_loses_tenant_context()
    {
        using var services = CreateServices();
        var tenant = services.GetRequiredService<ICurrentTenant>();
        using var scope = tenant.Change(Guid.NewGuid(), "Tenant A");
        using var cancellation = new CancellationTokenSource();

        var observed = await services.GetRequiredService<HybridCache>().GetOrCreateAsync(
            "cold", _ => ValueTask.FromResult(tenant.Id), cancellationToken: cancellation.Token);

        Assert.Null(observed);
        Assert.NotNull(tenant.Id);
    }

    [Fact]
    public async Task Tenant_cache_factory_restores_id_and_name_with_a_cancelable_token()
    {
        using var services = CreateServices();
        var tenant = services.GetRequiredService<ICurrentTenant>();
        var tenantId = Guid.NewGuid();
        using var scope = tenant.Change(tenantId, "Tenant A");
        using var cancellation = new CancellationTokenSource();

        var observed = await services.GetRequiredService<TenantHybridCache>().GetOrCreateAsync(
            "cold", async _ =>
            {
                await Task.Yield();
                return (tenant.Id, tenant.Name);
            }, cancellationToken: cancellation.Token);

        Assert.Equal(tenantId, observed.Id);
        Assert.Equal("Tenant A", observed.Name);
        Assert.Equal(tenantId, tenant.Id);
    }

    [Fact]
    public async Task Identical_keys_are_loaded_once_per_tenant_and_host()
    {
        using var services = CreateServices();
        var tenant = services.GetRequiredService<ICurrentTenant>();
        var cache = services.GetRequiredService<TenantHybridCache>();
        var calls = 0;
        ValueTask<string> Read(ReadOnlySpan<char> key) => cache.GetOrCreateAsync(key, _ =>
        {
            calls++;
            return ValueTask.FromResult(tenant.Id?.ToString("N") ?? "host");
        });

        Assert.Equal("host", await Read("shared".AsSpan()));
        foreach (var tenantId in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            using var scope = tenant.Change(tenantId);
            Assert.Equal(tenantId.ToString("N"), await Read("shared".AsSpan()));
            Assert.Equal(tenantId.ToString("N"), await Read("shared".AsSpan()));
        }

        Assert.Equal("host", await Read("shared".AsSpan()));
        Assert.Equal(3, calls);
        Assert.Equal("host", await services.GetRequiredService<HybridCache>().GetOrCreateAsync(
            "host:shared", static _ => ValueTask.FromResult("miss")));
    }

    [Fact]
    public async Task Setting_and_removing_keys_only_affects_the_current_tenant()
    {
        using var services = CreateServices();
        var tenant = services.GetRequiredService<ICurrentTenant>();
        var cache = services.GetRequiredService<TenantHybridCache>();
        var otherTenant = Guid.NewGuid();
        ValueTask<int> Read(string key) => cache.GetOrCreateAsync(key, static _ => ValueTask.FromResult(0));
        await cache.SetAsync("single", 1);
        using (tenant.Change(otherTenant))
        {
            await cache.SetAsync("single", 2);
            await cache.SetAsync("first", 2);
            await cache.SetAsync("second", 2);
        }

        using (tenant.Change(Guid.NewGuid()))
        {
            await cache.SetAsync("single", 3);
            await cache.SetAsync("first", 3);
            await cache.SetAsync("second", 3);
            Assert.Equal(3, await Read("single"));
            await cache.RemoveAsync("single");
            await cache.RemoveAsync(["first", "second"]);
            Assert.Equal(0, await Read("single"));
            Assert.Equal(0, await Read("first"));
            Assert.Equal(0, await Read("second"));
        }

        using (tenant.Change(otherTenant))
        {
            Assert.Equal(2, await Read("single"));
            Assert.Equal(2, await Read("first"));
            Assert.Equal(2, await Read("second"));
        }
        Assert.Equal(1, await Read("single"));
    }

    [Fact]
    public async Task Invalidating_tags_is_tenant_scoped_and_rejects_the_wildcard()
    {
        using var services = CreateServices();
        var tenant = services.GetRequiredService<ICurrentTenant>();
        var cache = services.GetRequiredService<TenantHybridCache>();
        ValueTask<int> Read(string key) => cache.GetOrCreateAsync(key, static _ => ValueTask.FromResult(0));
        await cache.SetAsync("single", 1, tags: ["group"]);
        await cache.SetAsync("batch", 1, tags: ["more"]);
        using (tenant.Change(Guid.NewGuid()))
        {
            await cache.GetOrCreateAsync("single", static _ => ValueTask.FromResult(2), tags: ["group"]);
            await cache.SetAsync("batch", 2, tags: ["more"]);
            await cache.SetAsync("untagged", 2);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await cache.RemoveByTagAsync("*"));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await cache.RemoveByTagAsync(["group", "*"]));
            Assert.Equal(2, await Read("single"));

            await cache.RemoveByTagAsync("group");
            await cache.RemoveByTagAsync(["more"]);
            Assert.Equal(0, await Read("single"));
            Assert.Equal(0, await Read("batch"));
            Assert.Equal(2, await Read("untagged"));
        }
        Assert.Equal(1, await Read("single"));
        Assert.Equal(1, await Read("batch"));
    }

    [Fact]
    public void Every_overridable_cache_member_is_implemented()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var members = typeof(HybridCache).GetMethods(flags).Where(method => method.IsVirtual);
        var overrides = typeof(TenantHybridCache).GetMethods(flags).Where(method => method.IsVirtual)
            .Select(method => method.GetBaseDefinition());

        Assert.Empty(members.Except(overrides));
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection().AddLogging().AddMultiTenancyCore();
        services.AddHybridCache();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
