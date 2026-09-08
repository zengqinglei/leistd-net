#if (!LocalIdentity)
using Leistd.Data;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Leistd.Data.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <remarks>
/// <b>远端依赖（<see cref="IIdentityTenantConnectionClient"/>、<see cref="ISecretResolver"/>）
/// 刻意不从构造注入。</b>它们只在单飞的共享任务里使用，而共享任务被设计成比发起者活得更久
/// （发起者取消不取消它），因此不能持有发起者请求作用域里的实例——那个作用域随时可能先释放。
/// 它们由 <see cref="TenantRouteResolutionCoordinator"/> 在自己的作用域里提供。
/// 这里注入的都是单例或每次取值的薄壳（配置、内存缓存、Options、当前租户）。
/// </remarks>
internal sealed class IdentityTenantConnectionStringResolver(
    ICurrentTenant currentTenant,
    IConfiguration configuration,
    IMemoryCache cache,
    IOptions<TenantRouteCacheOptions> routeCacheOptions,
    TenantRouteResolutionCoordinator coordinator) : IConnectionStringResolver
{

    // 可配置期限同时定义租户改路由前的排空等待时间。
    private TimeSpan CacheLifetime => routeCacheOptions.Value.CacheLifetime!.Value;

    public async Task<string> ResolveAsync(
        string connectionStringName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        var defaultConnection = configuration.GetConnectionString(connectionStringName);
        if (!currentTenant.IsAvailable)
        {
            return defaultConnection!;
        }

        var tenantId = currentTenant.Id!.Value;
        var cacheKey = $"tenant-connection:{tenantId:N}:{connectionStringName}";
        if (cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var lazy = coordinator.GetOrStart(
            cacheKey,
            (key, scopedServices) => ResolveSharedAsync(key, scopedServices, tenantId, defaultConnection));

        // 调用方只取消自己的等待，不取消其他调用方共享的回源任务。
        return await lazy.Value.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 单飞的共享解析。<b>不接受任何调用方的 <see cref="CancellationToken"/></b>
    /// </summary>
    /// <remarks>
    /// <para>共享任务的生命周期不能绑在任一调用方上：发起者取消会连带取消所有搭车者。
    /// 因此这里传 <see cref="CancellationToken.None"/>，取消由各调用方在 <c>WaitAsync</c>
    /// 侧各自决定。远端调用自身的超时由 <c>HttpClient.Timeout</c> 兜住，不会挂死。</para>
    /// <para>写缓存与摘除 inflight 都在这里、而不是在调用方的 <c>finally</c> 里：
    /// 放在调用方侧时，一个调用方取消就会把仍在飞的条目摘掉，
    /// 下一个调用方于是重新发起一次远端调用——单飞语义就没了。</para>
    /// <para><paramref name="scopedServices"/> 属于协调器为本共享任务新开的作用域，
    /// 远端依赖只从它取——见本类的类注释。</para>
    /// </remarks>
    private async Task<string> ResolveSharedAsync(
        string cacheKey,
        IServiceProvider scopedServices,
        Guid tenantId,
        string? defaultConnection)
    {
        try
        {
            var resolved = await ResolveRemoteAsync(scopedServices, tenantId, defaultConnection, CancellationToken.None);
            cache.Set(cacheKey, resolved, CacheLifetime);
            return resolved;
        }
        finally
        {
            // 只移除当前已完成的共享任务，不影响后续条目。
            coordinator.Complete(cacheKey);
        }
    }

    private static async Task<string> ResolveRemoteAsync(
        IServiceProvider scopedServices,
        Guid tenantId,
        string? defaultConnection,
        CancellationToken cancellationToken)
    {
        var identityClient = scopedServices.GetRequiredService<IIdentityTenantConnectionClient>();
        var secretResolver = scopedServices.GetRequiredService<ISecretResolver>();

        var configuration = await identityClient.GetRuntimeAsync(tenantId, cancellationToken);

        // 在解析 Secret 和写缓存前校验响应租户，防止错误结果造成跨租户数据访问。
        if (configuration.TenantId != tenantId)
        {
            throw new InternalServerException(
                $"Tenant connection lookup for '{tenantId}' returned a configuration for " +
                $"'{configuration.TenantId}'. Refusing to route a tenant to another tenant's database.");
        }

        return configuration.DatabaseMode switch
        {
            RemoteTenantDatabaseMode.SharedDatabase => defaultConnection!,
            RemoteTenantDatabaseMode.DedicatedDatabase when
                !string.IsNullOrWhiteSpace(configuration.RuntimeSecretReference) =>
                await secretResolver.ResolveAsync(configuration.RuntimeSecretReference, cancellationToken),
            // 独立库缺少 Secret 时失败关闭，不能回退到共享库。
            _ => throw new InternalServerException(
                $"Tenant '{tenantId}' connection configuration is corrupt: " +
                $"mode is {configuration.DatabaseMode} but the runtime Secret reference is missing.")
        };
    }
}
#endif
