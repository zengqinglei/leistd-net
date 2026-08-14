using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 基于 EF Core 的租户存储，经 <see cref="IDistributedCache"/> 缓存查询结果
/// </summary>
/// <remarks>
/// <para>缓存由 <see cref="ITenantManager"/> 在写入时失效；绕过管理器直接写库会留下陈旧缓存。</para>
/// <para>过期策略是**绝对**过期，不是滑动过期：租户的启用状态是访问控制状态，
/// 而 cache-aside 的失效是尽力而为的（Redis 不可用时 <c>RemoveAsync</c> 会失败）。
/// 滑动过期下，持续有流量的租户其陈旧的"启用"条目会被每个请求续命而永不过期——
/// 停用/删除的暴露窗口没有上界。绝对过期把失效失败的后果收成"最多
/// <see cref="CacheDuration"/> 后自愈"。</para>
/// </remarks>
public class EfCoreTenantStore<TDbContext>(TDbContext dbContext, IDistributedCache cache) : ITenantStore
    where TDbContext : DbContext
{
    /// <summary>
    /// 缓存条目存活时长，同时是停用/删除失效失败时的最大暴露窗口。
    /// </summary>
    /// <remarks>
    /// 取 1 分钟：租户解析每请求一次，1 分钟上界下一个 1000 req/min 的租户仍有 999 次命中，
    /// 代价可忽略；换来的是与 <c>ActiveUserRequirement</c>"每请求重读"同型的有界撤销语义。
    /// </remarks>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private static readonly DistributedCacheEntryOptions CacheEntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = CacheDuration
    };

    internal static string CacheKeyById(Guid id) => $"leistd:tenant:i:{id}";
    internal static string CacheKeyByName(string normalizedName) => $"leistd:tenant:n:{normalizedName}";

    /// <inheritdoc />
    public async Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await FindWithCacheAsync(
            CacheKeyById(id),
            () => Query().FirstOrDefaultAsync(t => t.Id == id, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        return await FindWithCacheAsync(
            CacheKeyByName(normalizedName),
            () => Query().FirstOrDefaultAsync(t => t.NormalizedName == normalizedName, cancellationToken),
            cancellationToken);
    }

    private IQueryable<TenantRecord> Query()
        // 显式排除软删除行：不依赖宿主 DbContext 是否配置了全局软删除过滤器
        => dbContext.Set<TenantRecord>().AsNoTracking().Where(t => !t.IsDeleted);

    private async Task<TenantConfiguration?> FindWithCacheAsync(
        string cacheKey,
        Func<Task<TenantRecord?>> query,
        CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<TenantConfiguration>(cached);
        }

        var record = await query();
        if (record is null)
        {
            return null; // 未命中不做负缓存：未知租户请求本身会被 404 短路
        }

        var configuration = ToConfiguration(record);
        await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(configuration), CacheEntryOptions, cancellationToken);
        return configuration;
    }

    /// <summary>
    /// 持久化实体 → Core 出参。管理器与存储共用，保证两条读路径的形状一致。
    /// </summary>
    internal static TenantConfiguration ToConfiguration(TenantRecord record) => new()
    {
        Id = record.Id,
        Name = record.Name,
        NormalizedName = record.NormalizedName,
        DisplayName = record.DisplayName,
        IsActive = record.IsActive,
        CreationTime = record.CreationTime
    };
}
