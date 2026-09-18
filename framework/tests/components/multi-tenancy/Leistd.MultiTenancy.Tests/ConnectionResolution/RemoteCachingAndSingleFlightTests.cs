using Xunit;

namespace Leistd.MultiTenancy.Tests.ConnectionResolution;

/// <summary>
/// 缓存与单飞：同租户同连接名的并发只回源一次，调用方取消只取消自己的等待。
/// </summary>
public sealed class RemoteCachingAndSingleFlightTests : IDisposable
{
    private readonly RemoteHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task A_resolved_route_is_served_from_cache()
    {
        _host.Tenant.Id = Guid.NewGuid();

        await _host.ResolveAsync();
        await _host.ResolveAsync();

        Assert.Equal(1, _host.Source.RuntimeCalls);
    }

    [Fact]
    public async Task Each_tenant_has_its_own_cache_entry()
    {
        _host.Tenant.Id = Guid.NewGuid();
        await _host.ResolveAsync();
        _host.Tenant.Id = Guid.NewGuid();
        await _host.ResolveAsync();

        Assert.Equal(2, _host.Source.RuntimeCalls);
    }

    // 缓存键带连接名：同一个租户在 crm 与 foundation 是两条路由，不能互相顶掉
    [Fact]
    public async Task Each_connection_name_has_its_own_cache_entry()
    {
        _host.Tenant.Id = Guid.NewGuid();
        _host.Source.Lookup = (id, name) => ScriptedRemoteSource.Hit(id, name, $"Data Source={name}");

        Assert.Equal("Data Source=crm", await _host.ResolveAsync("Crm"));
        Assert.Equal("Data Source=foundation", await _host.ResolveAsync("Foundation"));

        Assert.Equal(2, _host.Source.RuntimeCalls);
    }

    // 大小写只是写法差异，不能占两份缓存——否则一份命中一份回源，行为随调用点写法漂移
    [Fact]
    public async Task Names_differing_only_in_case_share_one_cache_entry()
    {
        _host.Tenant.Id = Guid.NewGuid();
        _host.Source.Lookup = (id, name) => ScriptedRemoteSource.Hit(id, name, "Data Source=acme-crm");

        await _host.ResolveAsync("Crm");
        await _host.ResolveAsync("crm");
        await _host.ResolveAsync("CRM");

        Assert.Equal(1, _host.Source.RuntimeCalls);
    }

    [Fact]
    public async Task Concurrent_requests_for_one_tenant_share_a_single_remote_call()
    {
        _host.Tenant.Id = Guid.NewGuid();
        _host.Source.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var callers = Enumerable.Range(0, 8).Select(_ => _host.ResolveAsync()).ToArray();
        await WaitUntilAsync(() => _host.Source.RuntimeCalls > 0);
        _host.Source.Gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, _host.Source.RuntimeCalls);
        Assert.All(results, result => Assert.Equal(RemoteHost.DefaultConnection, result));
    }

    // 发起者取消不能连带取消搭车者，也不能让下一个调用方重新回源
    [Fact]
    public async Task A_cancelled_caller_does_not_cancel_the_shared_resolution()
    {
        _host.Tenant.Id = Guid.NewGuid();
        _host.Source.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var cancelled = _host.ResolveAsync(cancellationToken: cts.Token);
        await WaitUntilAsync(() => _host.Source.RuntimeCalls > 0);
        var rider = _host.ResolveAsync();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        _host.Source.Gate.SetResult();

        Assert.Equal(RemoteHost.DefaultConnection, await rider);
        Assert.Equal(1, _host.Source.RuntimeCalls);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
