using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Leistd.Authorization.Resource.EntityFrameworkCore;

/// <summary>
/// EF Core 资源 ACL 存储。
/// </summary>
public class EfCoreResourceGrantStore<TDbContext>(TDbContext dbContext) : IResourceGrantStore
    where TDbContext : DbContext
{
    public async Task<ResourceGrantSet> GetGrantsAsync(
        string resourceName,
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        // 授予与版本是两次查询，中间夹进一次写入就会拼出"旧 ACL + 新版本"——拿它保存会被判为
        // 无冲突，显式拒绝就此被静默抹掉。用"读版本 → 读数据 → 再读版本"确认期间无人写入。
        const int MaxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var before = await ReadRevisionAsync();

            var grants = await dbContext.Set<ResourcePermissionGrantRecord>()
                .AsNoTracking()
                .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
                .Select(x => new ResourceGrant(x.Operation, x.ProviderName, x.ProviderKey, x.Effect))
                .ToListAsync(cancellationToken);

            var after = await ReadRevisionAsync();

            if (before == after)
                return new ResourceGrantSet(resourceName, resourceKey, grants, after);

            if (attempt >= MaxAttempts)
            {
                // 宁可失败也不返回撕裂的快照：调用方据此保存会静默丢失他人的修改。
                // 这是读取失败而不是保存冲突，不能复用带 expected/actual 语义的那个异常。
                throw new UnstableGrantSnapshotException($"{resourceName}/{resourceKey}", attempt);
            }
        }

        Task<long> ReadRevisionAsync()
            => dbContext.Set<ResourceAuthorizationRevisionRecord>()
                .AsNoTracking()
                .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
                .Select(x => x.Version)
                .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, ResourceGrantEffect>> GetEffectiveGrantsAsync(
        string resourceName,
        string resourceKey,
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default)
    {
        var roleKeys = Normalize(roleIds);

        var grants = await dbContext.Set<ResourcePermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.ResourceName == resourceName
                        && x.ResourceKey == resourceKey
                        && ((x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userId)
                            || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey))))
            .OrderBy(x => x.Id)
            .Select(x => new { x.Operation, x.Effect })
            .ToListAsync(cancellationToken);

        var effects = new Dictionary<string, ResourceGrantEffect>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            // 强度序：非法值 ≥ 拒绝 > 允许，强者粘滞。
            //
            // 只把 Prohibited 当粘滞值是不够的：同一 operation 上先出现一个损坏值、后出现 Granted 时，
            // 后者会把它覆盖成明确放行，而 SQL 没有排序，结果还随执行计划漂移。
            // 存储里出现读不懂的值时，唯一安全的解释是"不允许"。
            if (effects.TryGetValue(grant.Operation, out var existing) &&
                Strength(existing) >= Strength(grant.Effect))
            {
                continue;
            }

            effects[grant.Operation] = grant.Effect;
        }

        return effects;

        static int Strength(ResourceGrantEffect effect) => effect switch
        {
            ResourceGrantEffect.Granted => 0,
            ResourceGrantEffect.Prohibited => 1,
            _ => 2
        };
    }

    public IQueryable<string> QueryGrantedResourceKeys(
        string resourceName,
        string operation,
        string userId,
        IReadOnlyCollection<string> roleIds)
    {
        var roleKeys = Normalize(roleIds);
        var records = dbContext.Set<ResourcePermissionGrantRecord>().AsNoTracking();

        var subjectGrants = records.Where(x =>
            x.ResourceName == resourceName
            && x.Operation == operation
            && ((x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userId)
                || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey))));

        // 显式拒绝优先：先算出被拒绝的资源 Key，再从被允许的 Key 中排除。
        // 判据是"不是 Granted"而不是"等于 Prohibited"：读不懂的 effect 必须与拒绝同等对待，
        // 否则同一资源上再有一条 Granted，单实例判定会拒绝而列表却把它放出来——
        // 数据库约束只挡得住新写入，挡不住升级前就躺在库里的脏数据。
        var deniedKeys = subjectGrants
            .Where(x => x.Effect != ResourceGrantEffect.Granted)
            .Select(x => x.ResourceKey);

        return subjectGrants
            .Where(x => x.Effect == ResourceGrantEffect.Granted && !deniedKeys.Contains(x.ResourceKey))
            .Select(x => x.ResourceKey)
            .Distinct();
    }

    public IQueryable<string> QueryDeniedResourceKeys(
        string resourceName,
        string operation,
        string userId,
        IReadOnlyCollection<string> roleIds)
    {
        var roleKeys = Normalize(roleIds);

        // 与判定端同一判据：不是 Granted 就算拒绝，读不懂的 effect 一并计入。
        return dbContext.Set<ResourcePermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.ResourceName == resourceName
                        && x.Operation == operation
                        && x.Effect != ResourceGrantEffect.Granted
                        && ((x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userId)
                            || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey))))
            .Select(x => x.ResourceKey)
            .Distinct();
    }

    private static List<string> Normalize(IEnumerable<string> values)
        => values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>
/// EF Core 资源 ACL 管理器。
/// </summary>
public class EfCoreResourceGrantManager<TDbContext>(TDbContext dbContext) : IResourceGrantManager
    where TDbContext : DbContext
{
    public async Task<long> ReplaceGrantsAsync(
        string resourceName,
        string resourceKey,
        IReadOnlyCollection<ResourceGrant> grants,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceName) || string.IsNullOrWhiteSpace(resourceKey))
            throw new InvalidResourceGrantSubjectException(resourceName, resourceKey, "resource name and key are required");

        foreach (var grant in grants)
        {
            // 枚举可以承载任意底层值，判定端又必须 fail-closed：非法值一旦落库，
            // 该资源就静默变成"谁都不许访问"。在唯一写入口拒掉，最早也最好排查。
            if (!Enum.IsDefined(grant.Effect))
                throw new InvalidResourceGrantEffectException(resourceName, resourceKey, grant.Effect);

            // 读取端只按 User/Role 匹配主体：写进别的 ProviderName 或空标识的记录既不放行也不拒绝，
            // 只是永远匹配不上，成为查不出原因的脏数据。
            if (grant.ProviderName != PermissionGrantProviderNames.User &&
                grant.ProviderName != PermissionGrantProviderNames.Role)
            {
                throw new InvalidResourceGrantSubjectException(
                    resourceName, resourceKey, $"unknown provider '{grant.ProviderName}'");
            }

            if (string.IsNullOrWhiteSpace(grant.ProviderKey) || string.IsNullOrWhiteSpace(grant.Operation))
            {
                throw new InvalidResourceGrantSubjectException(
                    resourceName, resourceKey, "operation and provider key are required");
            }
        }

        var set = dbContext.Set<ResourcePermissionGrantRecord>();
        var revisionSet = dbContext.Set<ResourceAuthorizationRevisionRecord>();

        var revision = await revisionSet.FirstOrDefaultAsync(
            x => x.ResourceName == resourceName && x.ResourceKey == resourceKey,
            cancellationToken);

        var currentRevision = revision?.Version ?? 0;
        if (expectedRevision.HasValue && expectedRevision.Value != currentRevision)
        {
            throw new ResourceGrantConcurrencyException(
                resourceName,
                resourceKey,
                expectedRevision.Value,
                currentRevision);
        }

        var existing = await set
            .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
            .ToListAsync(cancellationToken);

        var target = new Dictionary<(string Operation, string ProviderName, string ProviderKey), ResourceGrantEffect>();
        foreach (var grant in grants)
        {
            var key = (grant.Operation, grant.ProviderName, grant.ProviderKey);

            // 同一三元组重复出现时拒绝优先。
            if (target.TryGetValue(key, out var current) && current == ResourceGrantEffect.Prohibited)
                continue;

            target[key] = grant.Effect;
        }

        // 记下本次改动的条目：写入失败时要精确还原它们的跟踪状态。DbContext 是宿主的工作单元，
        // 失败后把待写实体留在里面，调用方接住异常继续 SaveChanges 就会写出一份没有版本号护航的 ACL。
        var touched = new List<EntityEntry<ResourcePermissionGrantRecord>>();

        foreach (var record in existing)
        {
            var key = (record.Operation, record.ProviderName, record.ProviderKey);
            if (!target.TryGetValue(key, out var effect))
            {
                touched.Add(set.Remove(record));
            }
            else if (record.Effect != effect)
            {
                record.Effect = effect;
                touched.Add(dbContext.Entry(record));
            }
        }

        var existingKeys = existing
            .Select(x => (x.Operation, x.ProviderName, x.ProviderKey))
            .ToHashSet();

        foreach (var ((operation, providerName, providerKey), effect) in target)
        {
            if (existingKeys.Contains((operation, providerName, providerKey)))
                continue;

            touched.Add(set.Add(new ResourcePermissionGrantRecord
            {
                ResourceName = resourceName,
                ResourceKey = resourceKey,
                Operation = operation,
                ProviderName = providerName,
                ProviderKey = providerKey,
                Effect = effect
            }));
        }

        if (touched.Count == 0)
            return currentRevision;

        var isFirstWrite = revision == null;
        if (revision == null)
        {
            revision = new ResourceAuthorizationRevisionRecord
            {
                ResourceName = resourceName,
                ResourceKey = resourceKey,
                Version = 1
            };
            await revisionSet.AddAsync(revision, cancellationToken);
        }
        else
        {
            revision.Version += 1;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (InvolvesThisResource(ex))
        {
            DiscardPendingChanges();
            throw await ConflictAsync();
        }
        catch (DbUpdateException) when (isFirstWrite)
        {
            // 插入路径没有可比对的版本行，冲突表现为唯一索引违例，而唯一索引违例也可能来自
            // 其他约束。重新读一次，确认版本行确实已被他人建立才转成业务异常。
            DiscardPendingChanges();

            var conflict = await ConflictAsync();
            if (conflict.ActualRevision == 0)
                throw;

            throw conflict;
        }

        return revision.Version;

        bool InvolvesThisResource(DbUpdateConcurrencyException exception)
            => exception.Entries.Any(entry => entry.Entity switch
            {
                ResourceAuthorizationRevisionRecord record
                    => record.ResourceName == resourceName && record.ResourceKey == resourceKey,
                ResourcePermissionGrantRecord record
                    => record.ResourceName == resourceName && record.ResourceKey == resourceKey,
                _ => false
            });

        // 逐条还原本次动过的条目，绝不用 ChangeTracker.Clear()——那会连带解除与授权无关的实体。
        void DiscardPendingChanges()
        {
            foreach (var entry in touched)
            {
                // Modified → Unchanged 由 EF 负责把当前值还原成原值，无需手工 SetValues。
                entry.State = entry.State switch
                {
                    EntityState.Added => EntityState.Detached,
                    EntityState.Deleted => EntityState.Unchanged,
                    EntityState.Modified => EntityState.Unchanged,
                    _ => entry.State
                };
            }

            dbContext.Entry(revision!).State = EntityState.Detached;
        }

        async Task<ResourceGrantConcurrencyException> ConflictAsync()
        {
            var actualRevision = await dbContext.Set<ResourceAuthorizationRevisionRecord>()
                .AsNoTracking()
                .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
                .Select(x => x.Version)
                .FirstOrDefaultAsync(cancellationToken);

            return new ResourceGrantConcurrencyException(
                resourceName,
                resourceKey,
                expectedRevision ?? currentRevision,
                actualRevision);
        }
    }

    public async Task<int> RemoveResourceAsync(
        string resourceName,
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        // 授予行与版本行必须一起消失。两次独立的 ExecuteDelete 之间可以夹进一次 ReplaceGrantsAsync，
        // 留下"有授予行、版本为 0"的状态；资源 Key 被重用后旧 ACL 会重新生效，版本账本也失真。
        // 走变更跟踪器一次提交即可，一次 SaveChanges 就是一个事务——单个资源的 ACL 行数有界，
        // 为此在方法内部编排显式事务是这个方法撑不起的复杂度。
        var grants = await dbContext.Set<ResourcePermissionGrantRecord>()
            .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
            .ToListAsync(cancellationToken);

        var revisions = await dbContext.Set<ResourceAuthorizationRevisionRecord>()
            .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0 && revisions.Count == 0)
            return 0;

        dbContext.RemoveRange(grants);
        dbContext.RemoveRange(revisions);
        await dbContext.SaveChangesAsync(cancellationToken);

        return grants.Count;
    }
}
