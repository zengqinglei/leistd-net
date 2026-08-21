#if (ResourceService)
using System.Collections.Concurrent;
using Leistd.MultiTenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

internal sealed class IdentityTenantConnectionStringResolver(
    ICurrentTenant currentTenant,
    IIdentityTenantConnectionClient identityClient,
    ISecretResolver secretResolver,
    IConfiguration configuration,
    IMemoryCache cache) : ITenantConnectionStringResolver
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Inflight = new(StringComparer.Ordinal);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    public async Task<string> ResolveAsync(
        string connectionStringName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        var defaultConnection = configuration.GetConnectionString(connectionStringName);
        if (!currentTenant.IsAvailable)
        {
            return RequireDefaultConnection(defaultConnection);
        }

        var tenantId = currentTenant.Id!.Value;
        var cacheKey = $"tenant-connection:{tenantId:N}:{connectionStringName}";
        if (cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var lazy = Inflight.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<string>>(
                () => ResolveRemoteAsync(tenantId, defaultConnection, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var resolved = await lazy.Value;
            cache.Set(cacheKey, resolved, CacheLifetime);
            return resolved;
        }
        finally
        {
            Inflight.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(cacheKey, lazy));
        }
    }

    private async Task<string> ResolveRemoteAsync(
        Guid tenantId,
        string? defaultConnection,
        CancellationToken cancellationToken)
    {
        var configuration = await identityClient.GetRuntimeAsync(tenantId, cancellationToken);
        return configuration.DatabaseMode switch
        {
            RemoteTenantDatabaseMode.SharedDatabase => RequireDefaultConnection(defaultConnection),
            RemoteTenantDatabaseMode.DedicatedDatabase when
                !string.IsNullOrWhiteSpace(configuration.RuntimeSecretReference) =>
                await secretResolver.ResolveAsync(configuration.RuntimeSecretReference, cancellationToken),
            _ => throw new InvalidOperationException("The tenant connection configuration is invalid.")
        };
    }

    private static string RequireDefaultConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The host default connection string is not configured.");
        }

        return connectionString;
    }
}
#endif
