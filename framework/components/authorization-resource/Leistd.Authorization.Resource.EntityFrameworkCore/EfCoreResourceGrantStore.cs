using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.Resource.EntityFrameworkCore;

/// <summary>
/// EF Core 资源 ACL 存储。
/// </summary>
public class EfCoreResourceGrantStore<TDbContext>(TDbContext dbContext) : IResourceGrantStore
    where TDbContext : DbContext
{
    public async Task<IReadOnlyList<ResourceGrant>> GetGrantsAsync(
        string resourceName,
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<ResourcePermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
            .Select(x => new ResourceGrant(x.Operation, x.ProviderName, x.ProviderKey, x.Effect))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, PermissionGrantEffect>> GetEffectiveGrantsAsync(
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
            .Select(x => new { x.Operation, x.Effect })
            .ToListAsync(cancellationToken);

        var effects = new Dictionary<string, PermissionGrantEffect>(StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            // 拒绝优先。
            if (effects.TryGetValue(grant.Operation, out var existing) &&
                existing == PermissionGrantEffect.Prohibited)
            {
                continue;
            }

            effects[grant.Operation] = grant.Effect;
        }

        return effects;
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
        var deniedKeys = subjectGrants
            .Where(x => x.Effect == PermissionGrantEffect.Prohibited)
            .Select(x => x.ResourceKey);

        return subjectGrants
            .Where(x => x.Effect == PermissionGrantEffect.Granted && !deniedKeys.Contains(x.ResourceKey))
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
    public async Task ReplaceGrantsAsync(
        string resourceName,
        string resourceKey,
        IReadOnlyCollection<ResourceGrant> grants,
        CancellationToken cancellationToken = default)
    {
        var set = dbContext.Set<ResourcePermissionGrantRecord>();

        var existing = await set
            .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
            .ToListAsync(cancellationToken);

        var target = new Dictionary<(string Operation, string ProviderName, string ProviderKey), PermissionGrantEffect>();
        foreach (var grant in grants)
        {
            var key = (grant.Operation, grant.ProviderName, grant.ProviderKey);

            // 同一三元组重复出现时拒绝优先。
            if (target.TryGetValue(key, out var current) && current == PermissionGrantEffect.Prohibited)
                continue;

            target[key] = grant.Effect;
        }

        foreach (var record in existing)
        {
            var key = (record.Operation, record.ProviderName, record.ProviderKey);
            if (!target.TryGetValue(key, out var effect))
            {
                set.Remove(record);
            }
            else if (record.Effect != effect)
            {
                record.Effect = effect;
            }
        }

        var existingKeys = existing
            .Select(x => (x.Operation, x.ProviderName, x.ProviderKey))
            .ToHashSet();

        foreach (var ((operation, providerName, providerKey), effect) in target)
        {
            if (existingKeys.Contains((operation, providerName, providerKey)))
                continue;

            set.Add(new ResourcePermissionGrantRecord
            {
                ResourceName = resourceName,
                ResourceKey = resourceKey,
                Operation = operation,
                ProviderName = providerName,
                ProviderKey = providerKey,
                Effect = effect
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RemoveResourceAsync(
        string resourceName,
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<ResourcePermissionGrantRecord>()
            .Where(x => x.ResourceName == resourceName && x.ResourceKey == resourceKey)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
