using Leistd.Data.Connections;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.ConnectionStrings;

// 远端解析：向持有控制库的服务按连接名回源，结果按 TTL 缓存，同租户同名字的并发回源合并为一次。
//
// 远端依赖（ITenantConnectionConfigurationStore 的 HTTP 实现）刻意不从构造注入：它只在单飞的共享任务里使用，
// 而共享任务被设计成比发起者活得更久（发起者取消不取消它），不能持有发起者请求作用域里的实例。
// 它由协调器在自己的作用域里提供；这里注入的都是单例或每次取值的薄壳。
internal sealed class RemoteConnectionStringResolver(
    ICurrentTenant currentTenant,
    IConfiguration configuration,
    IMemoryCache cache,
    IOptions<TenantRouteCacheOptions> routeCacheOptions,
    TenantRouteResolutionCoordinator coordinator) : IConnectionStringResolver
{
    // 期限同时定义租户改路由前的排空等待时间；启动期已校验非空
    private TimeSpan CacheLifetime => routeCacheOptions.Value.CacheLifetime!.Value;

    public async Task<string> ResolveAsync(string connectionStringName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        if (!currentTenant.IsAvailable)
        {
            return TenantConnectionTargets.HostConnection(connectionStringName, configuration);
        }

        var tenantId = currentTenant.Id!.Value;

        // 归一化后再做缓存键：Crm 与 crm 是同一条路由，不能占两份缓存、也不能出现一份命中一份回源
        var name = TenantConnectionNames.Normalize(connectionStringName);
        var cacheKey = $"tenant-connection:{tenantId:N}:{name}";
        if (cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var lazy = coordinator.GetOrStart(
            cacheKey,
            (key, scopedServices) => ResolveSharedAsync(key, scopedServices, tenantId, connectionStringName, name));

        // 调用方只取消自己的等待，不取消其他调用方共享的回源任务
        return await lazy.Value.WaitAsync(cancellationToken);
    }

    // 单飞的共享解析，不接受任何调用方的取消令牌：发起者取消会连带取消所有搭车者。
    // 远端调用自身的超时由宿主客户端（HttpClient.Timeout 等）兜住。
    // 写缓存与摘除 inflight 都在这里而不在调用方的 finally：放在调用方侧时，一个调用方取消就会把
    // 仍在飞的条目摘掉，下一个调用方于是重新回源——单飞语义就没了。
    private async Task<string> ResolveSharedAsync(
        string cacheKey,
        IServiceProvider scopedServices,
        Guid tenantId,
        string connectionStringName,
        string normalizedName)
    {
        try
        {
            var resolved = await ResolveRemoteAsync(scopedServices, tenantId, connectionStringName, normalizedName);
            cache.Set(cacheKey, resolved, CacheLifetime);
            return resolved;
        }
        finally
        {
            coordinator.Complete(cacheKey);
        }
    }

    private async Task<string> ResolveRemoteAsync(
        IServiceProvider scopedServices,
        Guid tenantId,
        string connectionStringName,
        string normalizedName)
    {
        var store = scopedServices.GetRequiredService<ITenantConnectionConfigurationStore>();
        var lookup = await store.FindAsync(tenantId, normalizedName, CancellationToken.None)
            // 租户不存在或已删除。不可通过重试恢复，因此是 404 而不是 503；与本地解析同一判据
            ?? throw new NotFoundException($"Tenant '{tenantId}' was not found.");

        // 在使用与写缓存之前校验响应租户：错误的响应不能造成跨租户数据访问
        if (lookup.TenantId != tenantId)
        {
            throw new InternalServerException(
                $"Tenant connection lookup for '{tenantId}' returned a result for " +
                $"'{lookup.TenantId}'. Refusing to route a tenant to another tenant's database.");
        }

        return TenantConnectionTargets.Select(tenantId, connectionStringName, lookup, configuration);
    }
}
