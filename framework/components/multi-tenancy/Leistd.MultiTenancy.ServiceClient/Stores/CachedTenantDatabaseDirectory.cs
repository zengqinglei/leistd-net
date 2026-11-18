using Leistd.ServiceClient.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.ServiceClient.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.ServiceClient.Stores;

// 远端库目录的短时缓存：常驻服务的逐库作业按秒轮询，不能每轮都回源控制面。
// 全集与活跃清单分键，FailedTenants 随同一次回源的结果整体缓存。
// 远端存储在工厂里自开作用域取：工厂为所有等待者服务；按存储接口取再转目录，按目录接口取会拿到本类自己。
// 连接名来自 [ConnectionStringName]，不合法是编程错误，Normalize 直接抛 ArgumentException。
internal sealed class CachedTenantDatabaseDirectory(
    TenantRouteCache cache,
    IServiceScopeFactory scopeFactory,
    IOptions<RemoteTenantConnectionClientOptions> options,
    IOptions<TenantRouteCacheOptions> routeOptions,
    ILogger<CachedTenantDatabaseDirectory>? logger = null) : ITenantDatabaseDirectory
{
    public async Task<TenantDatabaseListResult> GetDatabasesAsync(
        string name,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        var normalized = TenantConnectionNames.Normalize(name);
        var lifetime = options.Value.DatabaseDirectoryCacheLifetime;

        return await cache.GetAsync($"leistd:tenant-databases:{normalized}:{(activeOnly ? "active" : "all")}", lifetime, routeOptions.Value.MaxStaleness,
                ct => ListRemoteAsync(normalized, activeOnly, ct),
                static ex => ex is ServiceClientException service && ServiceClientFailureClassifier.IsUnavailable(service),
                cancellationToken, logger ?? NullLogger<CachedTenantDatabaseDirectory>.Instance);
    }

    private async ValueTask<TenantDatabaseListResult> ListRemoteAsync(string name, bool activeOnly, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var directory = (ITenantDatabaseDirectory)scope.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationStore>();
        return await directory.GetDatabasesAsync(name, activeOnly, cancellationToken);
    }
}
