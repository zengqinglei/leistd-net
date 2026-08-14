using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 基于 EF Core 的租户存储，经 <see cref="IDistributedCache"/> 缓存查询结果
/// </summary>
/// <remarks>
/// 缓存由 <see cref="ITenantManager"/> 在写入时失效；绕过管理器直接写库会留下陈旧缓存。
/// 额外设置滑动过期作为漂移兜底。
/// </remarks>
public class EfCoreTenantStore<TDbContext>(TDbContext dbContext, IDistributedCache cache) : ITenantStore
    where TDbContext : DbContext
{
    // 滑动过期兜底：正常失效走 ITenantManager，这里只兜"绕过管理器写库"的漂移
    private static readonly DistributedCacheEntryOptions CacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(30)
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
