using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core 权限授予存储。
/// </summary>
/// <remarks>
/// 每个读取方法的数据库往返次数是常数（授予一次、版本一次），与被检查的权限数量和主体所属
/// 角色数量无关，因此不存在按权限或按角色的 N+1。
/// </remarks>
public class EfCorePermissionGrantStore<TDbContext>(TDbContext dbContext) : IPermissionGrantStore
    where TDbContext : DbContext
{
    public async Task<PermissionGrantSet> GetGrantsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(providerKey))
            return PermissionGrantSet.Empty(providerName, providerKey);

        var grants = await dbContext.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
            .Select(x => new PermissionGrant(x.PermissionName, x.Effect))
            .ToListAsync(cancellationToken);

        var revision = await dbContext.Set<AuthorizationRevisionRecord>()
            .AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
            .Select(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return new PermissionGrantSet(providerName, providerKey, grants, revision);
    }

    public async Task<IReadOnlyList<PermissionGrantSet>> GetGrantsAsync(
        string providerName,
        IReadOnlyCollection<string> providerKeys,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return [];

        var keys = providerKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
            return [];

        var grants = await dbContext.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.ProviderName == providerName && keys.Contains(x.ProviderKey))
            .Select(x => new { x.ProviderKey, x.PermissionName, x.Effect })
            .ToListAsync(cancellationToken);

        var revisions = await dbContext.Set<AuthorizationRevisionRecord>()
            .AsNoTracking()
            .Where(x => x.ProviderName == providerName && keys.Contains(x.ProviderKey))
            .Select(x => new { x.ProviderKey, x.Version })
            .ToListAsync(cancellationToken);

        var grantsByKey = grants
            .GroupBy(x => x.ProviderKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PermissionGrant>)[.. group.Select(x => new PermissionGrant(x.PermissionName, x.Effect))],
                StringComparer.Ordinal);

        var revisionByKey = revisions
            .ToDictionary(x => x.ProviderKey, x => x.Version, StringComparer.Ordinal);

        return
        [
            .. keys.Select(key => new PermissionGrantSet(
                providerName,
                key,
                grantsByKey.TryGetValue(key, out var keyGrants) ? keyGrants : [],
                revisionByKey.TryGetValue(key, out var version) ? version : 0))
        ];
    }

    public async Task<SubjectPermissionGrants> GetGrantsForSubjectAsync(
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default)
    {
        var userKey = userId ?? string.Empty;
        var roleKeys = roleIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var grants = await dbContext.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => (x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userKey)
                        || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey)))
            .Select(x => new { x.ProviderName, x.ProviderKey, x.PermissionName, x.Effect })
            .ToListAsync(cancellationToken);

        var revisions = await dbContext.Set<AuthorizationRevisionRecord>()
            .AsNoTracking()
            .Where(x => (x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userKey)
                        || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey)))
            .Select(x => new { x.ProviderName, x.ProviderKey, x.Version })
            .ToListAsync(cancellationToken);

        long RevisionOf(string providerName, string providerKey)
            => revisions
                .FirstOrDefault(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
                ?.Version ?? 0;

        List<PermissionGrant> GrantsOf(string providerName, string providerKey)
            => grants
                .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
                .Select(x => new PermissionGrant(x.PermissionName, x.Effect))
                .ToList();

        var userGrants = new PermissionGrantSet(
            PermissionGrantProviderNames.User,
            userKey,
            GrantsOf(PermissionGrantProviderNames.User, userKey),
            RevisionOf(PermissionGrantProviderNames.User, userKey));

        var roleGrants = roleKeys
            .Select(roleKey => new PermissionGrantSet(
                PermissionGrantProviderNames.Role,
                roleKey,
                GrantsOf(PermissionGrantProviderNames.Role, roleKey),
                RevisionOf(PermissionGrantProviderNames.Role, roleKey)))
            .ToList();

        return new SubjectPermissionGrants(userGrants, roleGrants);
    }
}
