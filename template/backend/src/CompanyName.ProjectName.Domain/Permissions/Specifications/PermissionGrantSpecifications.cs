using System.Linq.Expressions;
using CompanyName.ProjectName.Domain.Permissions.Entities;

namespace CompanyName.ProjectName.Domain.Permissions.Specifications;

/// <summary>
/// 权限授予规约（复杂的多字段条件，保留以提高可读性和复用性）
/// </summary>
public static class PermissionGrantSpecifications
{
    public static Expression<Func<PermissionGrant, bool>> ByPermissionAndProvider(
        string permissionName,
        string providerName,
        string providerKey)
    {
        return pg => pg.PermissionName == permissionName
                     && pg.ProviderName == providerName
                     && pg.ProviderKey == providerKey;
    }
}
