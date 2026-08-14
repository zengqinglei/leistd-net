using Leistd.Timing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Caching.Distributed;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// <see cref="ITenantManager"/> 的 EF Core 实现
/// </summary>
/// <remarks>
/// <para>契约与出参（<see cref="TenantConfiguration"/>）都在 Core：<see cref="TenantRecord"/>
/// 是本包的持久化实体，不出现在对外签名上，应用层无需引用任何持久化实现包。</para>
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
    public async Task<TenantConfiguration> CreateAsync(
        string name,
        string? displayName = null,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedName = normalizer.NormalizeName(name)!;
        await EnsureNameNotTakenAsync(normalizedName, excludeId: null, cancellationToken);

        var record = new TenantRecord
        {
            Name = name,
            NormalizedName = normalizedName,
            DisplayName = displayName,
            IsActive = isActive
        };

        var entry = dbContext.Set<TenantRecord>().Add(record);
        await SaveTranslatingDuplicateNameAsync(entry, normalizedName, cancellationToken);
        return EfCoreTenantStore<TDbContext>.ToConfiguration(record);
    }

    /// <inheritdoc />
    public async Task<TenantConfiguration> UpdateAsync(Guid id, string name, string? displayName, CancellationToken cancellationToken = default)
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

        await SaveTranslatingDuplicateNameAsync(dbContext.Entry(record), normalizedName, cancellationToken);
        await InvalidateCacheAsync(record.Id, oldNormalizedName, normalizedName, cancellationToken);
        return EfCoreTenantStore<TDbContext>.ToConfiguration(record);
    }

    /// <inheritdoc />
    public async Task<TenantConfiguration> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(id, cancellationToken);
        record.IsActive = isActive;

        await dbContext.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(record.Id, record.NormalizedName, null, cancellationToken);
        return EfCoreTenantStore<TDbContext>.ToConfiguration(record);
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
    public async Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Set<TenantRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);

        return record is null ? null : EfCoreTenantStore<TDbContext>.ToConfiguration(record);
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

        return new TenantPage(total, items.Select(EfCoreTenantStore<TDbContext>.ToConfiguration).ToList());
    }

    /// <summary>
    /// 保存并把名称唯一索引冲突翻译为 <see cref="DuplicateTenantNameException"/>。
    /// </summary>
    /// <remarks>
    /// 预检（<c>EnsureNameNotTakenAsync</c>）只能给出友好错误，挡不住并发——两个请求同时通过
    /// 校验时，由数据库的部分唯一索引兜住，落败方在这里得到与预检一致的异常，而不是 500。
    /// 判定必须排除本次写入的行：不排除的话，更新操作因其它约束（如显示名超长）失败时，
    /// 查同名会命中自己，把任何写入失败都误报成"名称重复"。
    /// </remarks>
    private async Task SaveTranslatingDuplicateNameAsync(
        EntityEntry<TenantRecord> entry,
        string normalizedName,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 只丢弃本次操作的条目。DbContext 可能是宿主的工作单元，
            // 清空整个跟踪器会连带丢掉调用方尚未提交的业务实体变更——
            // 调用方捕获名称冲突继续执行时，那些修改会静默消失。
            DiscardPendingChange(entry);

            // 判定用 AsNoTracking 直接打库，不受跟踪器状态影响
            var takenByOther = await dbContext.Set<TenantRecord>()
                .AsNoTracking()
                .AnyAsync(
                    t => t.NormalizedName == normalizedName && !t.IsDeleted && t.Id != entry.Entity.Id,
                    cancellationToken);

            if (takenByOther)
            {
                throw new DuplicateTenantNameException(normalizedName);
            }

            // 其它约束或数据库故障：原样上抛，不冒充名称冲突
            throw;
        }
    }

    /// <summary>
    /// 撤销本次操作在跟踪器里留下的待提交状态：新增条目脱离跟踪，
    /// 修改条目恢复原值——否则失败的改名会留在跟踪器里，被下一次保存写出去。
    /// </summary>
    private static void DiscardPendingChange(EntityEntry<TenantRecord> entry)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.State = EntityState.Detached;
                break;
            case EntityState.Modified:
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
                break;
        }
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
