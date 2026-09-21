using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.MultiTenancy.Tests.ConnectionResolution;

public sealed class SettableCurrentTenant : ICurrentTenant
{
    public Guid? Id { get; set; }
    public bool IsAvailable => Id.HasValue;
    public string? Name { get; private set; }

    /// <summary>Change 期间被观察到的租户，用于断言"是在宿主视角下解析的"。</summary>
    public List<Guid?> Observed { get; } = [];

    public IDisposable Change(Guid? id, string? name = null)
    {
        var previousId = Id;
        var previousName = Name;
        Id = id;
        Name = name;
        Observed.Add(id);
        return new Restore(() =>
        {
            Id = previousId;
            Name = previousName;
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

/// <summary>
/// 可编排的远端连接存储（宿主 HTTP 实现的替身）：按名字问、按名字答，记调用次数，
/// 可用闸门把回源挂住以制造并发。
/// </summary>
public sealed class ScriptedRemoteSource : ITenantConnectionConfigurationStore
{
    /// <summary>默认行为：租户存在但一条连接都没登记 —— 即"不分库"，用服务自己的配置。</summary>
    public Func<Guid, string, TenantConnectionLookupResult?> Lookup { get; set; } =
        (id, _) => new TenantConnectionLookupResult { TenantId = id, HasAnyConnection = false };

    public List<TenantMigrationConnection> Migration { get; } = [];
    public TaskCompletionSource? Gate { get; set; }
    public int RuntimeCalls;

    /// <summary>实现收到的名字，用于断言解析器传下来的是归一化后的值。</summary>
    public List<string> RequestedNames { get; } = [];

    public async Task<TenantConnectionLookupResult?> FindAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref RuntimeCalls);
        lock (RequestedNames)
        {
            RequestedNames.Add(name);
        }

        if (Gate is not null)
        {
            await Gate.Task;
        }

        return Lookup(tenantId, name);
    }

    public Task<IReadOnlyList<TenantMigrationConnection>> GetListAsync(
        string name,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TenantMigrationConnection>>(Migration);

    /// <summary>构造一条命中的登记。</summary>
    public static TenantConnectionLookupResult Hit(Guid tenantId, string name, string connectionString) => new()
    {
        TenantId = tenantId,
        HasAnyConnection = true,
        Connection = new TenantConnectionConfiguration
        {
            TenantId = tenantId,
            Name = name,
            ConnectionString = connectionString,
            Version = 1
        }
    };

    /// <summary>租户登记过连接，但没有这个名字、也没有默认名可回落。</summary>
    public static TenantConnectionLookupResult RegisteredButMissing(Guid tenantId) => new()
    {
        TenantId = tenantId,
        HasAnyConnection = true,
        Connection = null
    };
}

/// <summary>按真实注册路径组装远端解析的宿主。</summary>
public sealed class RemoteHost : IDisposable
{
    public const string DefaultConnection = "Data Source=shared";

    public SettableCurrentTenant Tenant { get; } = new();
    public ScriptedRemoteSource Source { get; } = new();
    public ServiceProvider Provider { get; }

    public RemoteHost(
        string? cacheLifetime = "00:05:00",
        string? defaultConnection = DefaultConnection,
        string? namedConnection = null,
        string namedConnectionName = "Crm")
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = defaultConnection,
            ["TenantRouting:CacheLifetime"] = cacheLifetime
        };
        if (namedConnection is not null)
        {
            settings[$"ConnectionStrings:{namedConnectionName}"] = namedConnection;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddSingleton<ITenantConnectionConfigurationStore>(Source);
        services.AddRemoteTenantConnectionResolution();
        Provider = services.BuildServiceProvider();
    }

    public async Task<string> ResolveAsync(string name = "Default", CancellationToken cancellationToken = default)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Leistd.Data.Connections.IConnectionStringResolver>()
            .ResolveAsync(name, cancellationToken);
    }

    public void Dispose() => Provider.Dispose();
}
