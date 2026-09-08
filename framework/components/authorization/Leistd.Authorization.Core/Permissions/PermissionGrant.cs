using Leistd.Authorization.Constants;
using Leistd.Authorization.Abstractions;

namespace Leistd.Authorization.Permissions;

/// <summary>
/// 单个授予主体（用户或角色）的全部授予及其并发版本。
/// </summary>
/// <param name="ProviderName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
/// <param name="ProviderKey">授予对象 Key，如 UserId、RoleId。</param>
/// <param name="PermissionNames">该主体已授予的权限名。</param>
/// <param name="Version">该主体的授权版本，每次写入递增；从未写入过时为 0。</param>
public sealed record PermissionGrantSet(
    string ProviderName,
    string ProviderKey,
    IReadOnlyList<string> PermissionNames,
    long Version)
{
    /// <summary>
    /// 构造一个没有任何授予的空集合。
    /// </summary>
    public static PermissionGrantSet Empty(string providerName, string providerKey)
        => new(providerName, providerKey, [], 0);
}

/// <summary>
/// 权限检查主体的全部授予：用户直授加其所属角色的授予。
/// </summary>
/// <param name="UserGrants">用户直接授予。</param>
/// <param name="RoleGrants">用户所属各角色的授予，按角色一个集合。</param>
public sealed record SubjectPermissionGrants(
    PermissionGrantSet UserGrants,
    IReadOnlyList<PermissionGrantSet> RoleGrants)
{
    /// <summary>
    /// 主体有效权限的版本标记。
    /// </summary>
    /// <remarks>
    /// 客户端可用它判断本地缓存的有效权限是否过期。
    /// 角色成员变更会改变参与拼接的角色集合，因此无需为成员变更额外扇出写入。
    /// </remarks>
    public string VersionToken { get; } = BuildVersionToken(UserGrants, RoleGrants);

    /// <summary>
    /// 主体最终拥有的权限名：所有来源取并集。
    /// </summary>
    public IReadOnlySet<string> GetGrantedNames()
    {
        var granted = new HashSet<string>(UserGrants.PermissionNames, StringComparer.Ordinal);
        foreach (var roleGrants in RoleGrants)
        {
            granted.UnionWith(roleGrants.PermissionNames);
        }

        return granted;
    }

    private static string BuildVersionToken(
        PermissionGrantSet userGrants,
        IReadOnlyList<PermissionGrantSet> roleGrants)
    {
        var roles = roleGrants
            .OrderBy(x => x.ProviderKey, StringComparer.Ordinal)
            .Select(x => $"{x.ProviderKey}:{x.Version}");

        return $"u{userGrants.Version}|r{string.Join(',', roles)}";
    }
}
