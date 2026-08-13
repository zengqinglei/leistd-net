using Leistd.Exception.Core;
using Leistd.Timing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// <see cref="ITenantManager"/> 的 EF Core 实现
/// </summary>
/// <remarks>
/// <para>管理器自行 SaveChanges（租户管理是独立的低频管理操作）；
/// 在外层工作单元事务内调用时仅表现为提前刷写，不破坏事务边界。</para>
/// <para>软删除由管理器自己落标记（不经 <c>Remove()</c> 依赖审计拦截器转换）：
/// 宿主未挂载审计拦截器时删除租户也绝不能退化成物理删除。
/// <c>DeleterId</c> 不在此填充——那需要用户上下文，归审计层职责。</para>
/// </remarks>
public class EfCoreTenantManager<TDbContext>(
    TDbContext dbContext,
    ITenantNormalizer normalizer,
    IDistributedCache cache,
    IClock clock) : ITenantManager
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<TenantRecord> CreateAsync(string name, string? displayName = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedName = normalizer.NormalizeName(name)!;
        await EnsureNameNotTakenAsync(normalizedName, excludeId: null, cancellationToken);

        var record = new TenantRecord
        {
            Name = name,
            NormalizedName = normalizedName,
            DisplayName = displayName
        };

        dbContext.Set<TenantRecord>().Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }

    /// <inheritdoc />
    public async Task<TenantRecord> UpdateAsync(Guid id, string name, string? displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var record = await GetAsync(id, cancellationToken);
        var normalizedName = normalizer.NormalizeName(name)!;

        if (!string.Equals(record.NormalizedName, normalizedName, StringComparison.Ordinal))
        {
            await EnsureNameNotTakenAsync(normalizedName, excludeId: id, cancellationToken);
        }

        var oldNormalizedName = record.NormalizedName;
        record.Name = name;
        record.NormalizedName = normalizedName;
        record.DisplayName = displayName;

        await dbContext.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(record.Id, oldNormalizedName, normalizedName, cancellationToken);
        return record;
    }

    /// <inheritdoc />
    public async Task<TenantRecord> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(id, cancellationToken);
        record.IsActive = isActive;

        await dbContext.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(record.Id, record.NormalizedName, null, cancellationToken);
        return record;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(id, cancellationToken);

        // 显式软删除：不经 Remove()，删除语义不依赖宿主是否挂载审计拦截器
        record.IsDeleted = true;
        record.DeletionTime = clock.Now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(record.Id, record.NormalizedName, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TenantRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<TenantRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TenantPage> GetPagedAsync(string? keyword, int offset, int limit, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<TenantRecord>().AsNoTracking().Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = normalizer.NormalizeName(keyword)!;
            query = query.Where(t =>
                t.NormalizedName.Contains(normalizedKeyword) ||
                (t.DisplayName != null && t.DisplayName.Contains(keyword)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.CreationTime)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new TenantPage(total, items);
    }

    private async Task<TenantRecord> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Set<TenantRecord>()
                   .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken)
               ?? throw new TenantNotFoundException(id.ToString());
    }

    private async Task EnsureNameNotTakenAsync(string normalizedName, Guid? excludeId, CancellationToken cancellationToken)
    {
        var taken = await dbContext.Set<TenantRecord>()
            .AnyAsync(t => t.NormalizedName == normalizedName && !t.IsDeleted && (excludeId == null || t.Id != excludeId),
                cancellationToken);

        if (taken)
        {
            throw new DuplicateTenantNameException(normalizedName);
        }
    }

    private async Task InvalidateCacheAsync(Guid id, string oldNormalizedName, string? newNormalizedName, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(EfCoreTenantStore<TDbContext>.CacheKeyById(id), cancellationToken);
        await cache.RemoveAsync(EfCoreTenantStore<TDbContext>.CacheKeyByName(oldNormalizedName), cancellationToken);

        if (newNormalizedName is not null && !string.Equals(newNormalizedName, oldNormalizedName, StringComparison.Ordinal))
        {
            await cache.RemoveAsync(EfCoreTenantStore<TDbContext>.CacheKeyByName(newNormalizedName), cancellationToken);
        }
    }
}

/// <summary>
/// 租户名称冲突异常。继承 <see cref="ConflictException"/>，经全局异常处理器映射为 HTTP 409
/// </summary>
/// <param name="normalizedName">冲突的归一化名称</param>
public class DuplicateTenantNameException(string normalizedName)
    : ConflictException($"Tenant name already exists: {normalizedName}")
{
    /// <summary>
    /// 冲突的归一化名称
    /// </summary>
    public string NormalizedName { get; } = normalizedName;
}
