using Leistd.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core 权限授予管理器。
/// </summary>
public class EfCorePermissionGrantManager<TDbContext>(
    TDbContext dbContext,
    IPermissionGrantStore permissionGrantStore) : IPermissionGrantManager
    where TDbContext : DbContext
{
    public Task GrantToUserAsync(
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return GrantAsync(
            permissionName,
            PermissionGrantProviderNames.User,
            userId,
            cancellationToken);
    }

    public Task GrantToRoleAsync(
        string permissionName,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        return GrantAsync(
            permissionName,
            PermissionGrantProviderNames.Role,
            roleId,
            cancellationToken);
    }

    public Task RevokeFromUserAsync(
        string permissionName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return RevokeAsync(
            permissionName,
            PermissionGrantProviderNames.User,
            userId,
            cancellationToken);
    }

    public Task RevokeFromRoleAsync(
        string permissionName,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        return RevokeAsync(
            permissionName,
            PermissionGrantProviderNames.Role,
            roleId,
            cancellationToken);
    }

    public Task<IReadOnlySet<string>> GetGrantedPermissionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return permissionGrantStore.GetGrantedPermissionsForUserAsync(userId, cancellationToken);
    }

    public Task<IReadOnlySet<string>> GetGrantedPermissionsForRoleAsync(
        string roleId,
        CancellationToken cancellationToken = default)
    {
        return permissionGrantStore.GetGrantedPermissionsForRoleAsync(roleId, cancellationToken);
    }

    private async Task GrantAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
    {
        Validate(permissionName, nameof(permissionName));
        Validate(providerKey, nameof(providerKey));

        var exists = await dbContext.Set<PermissionGrantRecord>().AnyAsync(
            x => x.PermissionName == permissionName
                 && x.ProviderName == providerName
                 && x.ProviderKey == providerKey,
            cancellationToken);

        if (exists)
            return;

        dbContext.Set<PermissionGrantRecord>().Add(new PermissionGrantRecord
        {
            PermissionName = permissionName,
            ProviderName = providerName,
            ProviderKey = providerKey
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken)
    {
        Validate(permissionName, nameof(permissionName));
        Validate(providerKey, nameof(providerKey));

        var grants = await dbContext.Set<PermissionGrantRecord>()
            .Where(x => x.PermissionName == permissionName
                        && x.ProviderName == providerName
                        && x.ProviderKey == providerKey)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
            return;

        dbContext.Set<PermissionGrantRecord>().RemoveRange(grants);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("值不能为空。", parameterName);
    }
}
