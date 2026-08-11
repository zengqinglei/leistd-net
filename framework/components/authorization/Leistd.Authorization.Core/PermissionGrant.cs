namespace Leistd.Authorization;

/// <summary>
/// 单个授予主体（用户或角色）的全部授予及其并发版本。
/// </summary>
/// <remarks>
/// 授予是纯加法：存在记录即授予，不存在即未授予，没有"显式拒绝"。
/// 多个来源（用户直授与各角色）之间取并集，因此"某人能做 X"永远等价于
/// "他的某个来源授予了 X"——这条等价关系是权限可审计、可解释的前提。
/// 需要收回能力时改角色构成，而不是在权限位上做减法。
/// </remarks>
/// <param name="ProviderName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
/// <param name="ProviderKey">授予对象 Key，如 UserId、RoleId。</param>
/// <param name="PermissionNames">该主体已授予的权限名。</param>
/// <param name="Revision">该主体的授权版本，每次写入递增；从未写入过时为 0。</param>
public sealed record PermissionGrantSet(
    string ProviderName,
    string ProviderKey,
    IReadOnlyList<string> PermissionNames,
    long Revision)
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
/// <remarks>
/// 由 <see cref="IPermissionGrantStore.GetGrantsForSubjectAsync"/> 一次性取回，
/// 后续的合并与多权限判断全部在内存完成，不再回访存储。
/// </remarks>
/// <param name="UserGrants">用户直接授予。</param>
/// <param name="RoleGrants">用户所属各角色的授予，按角色一个集合。</param>
public sealed record SubjectPermissionGrants(
    PermissionGrantSet UserGrants,
    IReadOnlyList<PermissionGrantSet> RoleGrants)
{
    /// <summary>
    /// 主体授权版本。
    /// </summary>
    /// <remarks>
    /// 由用户授予版本与各角色授予版本（按角色 Key 序数排序）拼接而成。角色成员变更会改变
    /// 参与拼接的角色集合，因此无需为成员变更额外扇出写入即可反映在版本中。
    /// 客户端可用它判断本地缓存的有效权限是否过期。
    /// </remarks>
    public string Revision { get; } = BuildRevision(UserGrants, RoleGrants);

    /// <summary>
    /// 主体最终拥有的权限名：所有来源取并集。
    /// </summary>
    /// <remarks>
    /// 写入时已把被授予权限的祖先补齐，因此这里直接取并集即可，无需回溯定义树。
    /// </remarks>
    public IReadOnlySet<string> GetGrantedNames()
    {
        var granted = new HashSet<string>(UserGrants.PermissionNames, StringComparer.Ordinal);
        foreach (var roleGrants in RoleGrants)
        {
            granted.UnionWith(roleGrants.PermissionNames);
        }

        return granted;
    }

    private static string BuildRevision(
        PermissionGrantSet userGrants,
        IReadOnlyList<PermissionGrantSet> roleGrants)
    {
        var roles = roleGrants
            .OrderBy(x => x.ProviderKey, StringComparer.Ordinal)
            .Select(x => $"{x.ProviderKey}:{x.Revision}");

        return $"u{userGrants.Revision}|r{string.Join(',', roles)}";
    }
}
