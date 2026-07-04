using Leistd.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core 权限授予存储。
/// </summary>
public class EfCorePermissionGrantStore<TDbContext>(TDbContext dbContext) : IPermissionGrantStore
    where TDbContext : DbContext
{
    public Task<bool> IsGrantedToUserAsync(
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Set<PermissionGrantRecord>().AnyAsync(
            x => x.PermissionName == permissionName
                 && x.ProviderName == PermissionGrantProviderNames.User
                 && x.ProviderKey == userId,
            cancellationToken);
    }

    public Task<bool> IsGrantedToRoleAsync(
        string permissionName,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Set<PermissionGrantRecord>().AnyAsync(
            x => x.PermissionName == permissionName
                 && x.ProviderName == PermissionGrantProviderNames.Role
                 && x.ProviderKey == roleId,
            cancellationToken);
    }

    public Task<bool> IsGrantedToAnyRoleAsync(
        string permissionName,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default)
    {
        var roleKeys = Normalize(roleIds);
        if (roleKeys.Count == 0)
            return Task.FromResult(false);

        return dbContext.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .AnyAsync(
                x => x.PermissionName == permissionName
                     && x.ProviderName == PermissionGrantProviderNames.Role
                     && roleKeys.Contains(x.ProviderKey),
                cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, bool>> IsGrantedToUserOrRolesAsync(
        IReadOnlyCollection<string> permissionNames,
        string userId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default)
    {
        var permissions = Normalize(permissionNames);
        if (permissions.Count == 0)
            return new Dictionary<string, bool>();

        var roleKeys = Normalize(roleIds);
        var grantedPermissions = await dbContext.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => permissions.Contains(x.PermissionName)
                        && ((x.ProviderName == PermissionGrantProviderNames.User && x.ProviderKey == userId)
                            || (x.ProviderName == PermissionGrantProviderNames.Role && roleKeys.Contains(x.ProviderKey))))
            .Select(x => x.PermissionName)
            .Distinct()
            .ToListAsync(cancellationToken);

        var grantedSet = grantedPermissions.ToHashSet(StringComparer.Ordinal);
        return permissions.ToDictionary(x => x, x => grantedSet.Contains(x), StringComparer.Ordinal);
    }

    public Task<IReadOnlySet<string>> GetGrantedPermissionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return GetGrantedPermissionsAsync(
            PermissionGrantProviderNames.User,
            userId,
            cancellationToken);
    }

    public Task<IReadOnlySet<string>> GetGrantedPermissionsForRoleAsync(
        string roleId,
        CancellationToken cancellationToken = default)
    {
        return GetGrantedPermissionsAsync(
            PermissionGrantProviderNames.Role,
            roleId,
            cancellationToken);
    }

    private async Task<IReadOnlySet<string>> GetGrantedPermissionsAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var permissions = await dbContext.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.ProviderName == providerName && x.ProviderKey == providerKey)
            .Select(x => x.PermissionName)
            .Distinct()
            .ToListAsync(cancellationToken);

        return permissions.ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> Normalize(IEnumerable<string> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
