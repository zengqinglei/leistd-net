using Leistd.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core 权限授予存储。
/// </summary>
public class EfCorePermissionGrantStore<TDbContext>(TDbContext dbContext) : IPermissionGrantStore
    where TDbContext : DbContext
{
    public Task<bool> IsGrantedAsync(
        string permissionName,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Set<PermissionGrantRecord>().AnyAsync(
            x => x.PermissionName == permissionName
                 && x.ProviderName == providerName
                 && x.ProviderKey == providerKey,
            cancellationToken);
    }
}

