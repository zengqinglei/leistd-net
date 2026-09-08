namespace Leistd.MultiTenancy.Stores;

/// <summary>
/// 表示与存储实现无关的租户配置快照。
/// </summary>
public class TenantConfiguration
{
    /// <summary>获取租户标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>获取大小写不敏感的唯一租户名称。</summary>
    public required string Name { get; init; }

    /// <summary>获取由 <see cref="ITenantNormalizer"/> 生成的查询键。</summary>
    public required string NormalizedName { get; init; }

    /// <summary>获取可选的显示名称。</summary>
    public string? DisplayName { get; init; }

    /// <summary>获取租户是否启用。</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>获取创建时间。</summary>
    public DateTime CreationTime { get; init; }
}
