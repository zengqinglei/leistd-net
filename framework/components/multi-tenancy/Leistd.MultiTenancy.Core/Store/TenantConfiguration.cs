namespace Leistd.MultiTenancy;

/// <summary>
/// 租户配置快照：<see cref="ITenantStore"/> 与 <see cref="ITenantManager"/> 的共用出参，与存储实现解耦
/// </summary>
public class TenantConfiguration
{
    /// <summary>租户 Id</summary>
    public required Guid Id { get; init; }

    /// <summary>租户名称（业务标识，大小写不敏感唯一）</summary>
    public required string Name { get; init; }

    /// <summary>归一化名称（查询键，由 <see cref="ITenantNormalizer"/> 生成）</summary>
    public required string NormalizedName { get; init; }

    /// <summary>显示名称（可选）</summary>
    public string? DisplayName { get; init; }

    /// <summary>是否启用。停用租户的请求会被中间件拒绝</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>创建时间（管理界面展示用）</summary>
    public DateTime CreationTime { get; init; }
}
