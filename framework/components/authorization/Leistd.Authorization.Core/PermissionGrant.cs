namespace Leistd.Authorization;

/// <summary>
/// 权限授予效果。
/// </summary>
/// <remarks>
/// 未授予（Undefined）由"没有授予记录"表达，因此存储层只有显式允许与显式拒绝两个取值。
/// 同一权限同时存在允许与拒绝时，拒绝优先。
/// </remarks>
public enum PermissionGrantEffect
{
    /// <summary>显式允许。</summary>
    Granted = 1,

    /// <summary>显式拒绝。优先于任何来源的允许。</summary>
    Prohibited = 2
}

/// <summary>
/// 单条权限授予。
/// </summary>
/// <param name="PermissionName">权限名称。</param>
/// <param name="Effect">授予效果。</param>
public sealed record PermissionGrant(string PermissionName, PermissionGrantEffect Effect);

/// <summary>
/// 单个授予主体（用户或角色）的全部授予及其并发版本。
/// </summary>
/// <param name="ProviderName">授予对象类型，见 <see cref="PermissionGrantProviderNames"/>。</param>
/// <param name="ProviderKey">授予对象 Key，如 UserId、RoleId。</param>
/// <param name="Grants">该主体的全部授予记录。</param>
/// <param name="Revision">该主体的授权版本，每次写入递增；从未写入过时为 0。</param>
public sealed record PermissionGrantSet(
    string ProviderName,
    string ProviderKey,
    IReadOnlyList<PermissionGrant> Grants,
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
/// 后续的三态组合与多权限判断全部在内存完成，不再回访存储。
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
    /// 计算主体对每个权限的最终授予效果：任一来源为拒绝即拒绝，否则任一来源为允许即允许。
    /// </summary>
    public IReadOnlyDictionary<string, PermissionGrantEffect> GetEffectiveEffects()
    {
        var effects = new Dictionary<string, PermissionGrantEffect>(StringComparer.Ordinal);

        Merge(effects, UserGrants);
        foreach (var roleGrants in RoleGrants)
        {
            Merge(effects, roleGrants);
        }

        return effects;
    }

    private static void Merge(
        Dictionary<string, PermissionGrantEffect> effects,
        PermissionGrantSet grantSet)
    {
        foreach (var grant in grantSet.Grants)
        {
            // 拒绝优先：已经是拒绝的不再被允许覆盖。
            if (effects.TryGetValue(grant.PermissionName, out var existing) &&
                existing == PermissionGrantEffect.Prohibited)
            {
                continue;
            }

            effects[grant.PermissionName] = grant.Effect;
        }
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
