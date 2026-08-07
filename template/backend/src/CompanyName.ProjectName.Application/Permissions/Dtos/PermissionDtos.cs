#if (IncludeRoles)
using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Permissions.Dtos;

/// <summary>
/// 当前用户的有效权限。
/// </summary>
/// <remarks>
/// 前端据此裁剪路由、菜单和按钮；裁剪只影响体验，服务端仍会对每个请求独立校验。
/// </remarks>
public record CurrentPermissionsOutputDto
{
    /// <summary>已生效的权限名集合（已扣除显式拒绝）。</summary>
    [Display(Name = "Permissions")]
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>是否超级管理员（旁路全部功能权限）。</summary>
    [Display(Name = "Super administrator")]
    public bool IsSuperAdmin { get; init; }

    /// <summary>授权版本。变化即表示本地缓存的权限已过期。</summary>
    [Display(Name = "Revision")]
    public required string Revision { get; init; }
}

/// <summary>
/// 权限定义（树节点）。
/// </summary>
public record PermissionDefinitionOutputDto
{
    [Display(Name = "Permission name")]
    public required string Name { get; init; }

    [Display(Name = "Display name")]
    public required string DisplayName { get; init; }

    [Display(Name = "Parent permission")]
    public string? ParentName { get; init; }

    [Display(Name = "Child permissions")]
    public required IReadOnlyList<PermissionDefinitionOutputDto> Children { get; init; }
}

/// <summary>
/// 权限定义分组。
/// </summary>
public record PermissionDefinitionGroupOutputDto
{
    [Display(Name = "Group name")]
    public required string Name { get; init; }

    [Display(Name = "Display name")]
    public required string DisplayName { get; init; }

    [Display(Name = "Permissions")]
    public required IReadOnlyList<PermissionDefinitionOutputDto> Permissions { get; init; }
}

/// <summary>
/// 单个权限在某主体上的授予状态。
/// </summary>
public record PermissionGrantStateDto
{
    [Display(Name = "Permission name")]
    public required string Name { get; init; }

    /// <summary>该主体自身的直接授予：Granted / Prohibited / 未设置时为 null。</summary>
    [Display(Name = "Direct grant")]
    public string? Direct { get; init; }

    /// <summary>从角色继承而来的授予（仅用户主体有值）。</summary>
    [Display(Name = "Inherited grant")]
    public string? Inherited { get; init; }

    /// <summary>综合直接授予与继承后的最终结果。</summary>
    [Display(Name = "Effective")]
    public bool Effective { get; init; }
}

/// <summary>
/// 某个主体的权限授予及其并发版本。
/// </summary>
public record PermissionGrantsOutputDto
{
    [Display(Name = "Subject type")]
    public required string ProviderName { get; init; }

    [Display(Name = "Subject key")]
    public required string ProviderKey { get; init; }

    /// <summary>乐观并发版本，保存时原样回传。</summary>
    [Display(Name = "Revision")]
    public long Revision { get; init; }

    [Display(Name = "Grants")]
    public required IReadOnlyList<PermissionGrantStateDto> Grants { get; init; }
}

/// <summary>
/// 单条待保存的授予。
/// </summary>
public record PermissionGrantInputDto
{
    [Display(Name = "Permission name")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string Name { get; init; }

    /// <summary>
    /// 授予效果：<c>Granted</c> 或 <c>Prohibited</c>。未出现在集合中的权限即为"继承/未设置"。
    /// </summary>
    [Display(Name = "Effect")]
    [Required(ErrorMessage = "{0} is required.")]
    [RegularExpression("^(Granted|Prohibited)$", ErrorMessage = "{0} must be either Granted or Prohibited.")]
    public required string Effect { get; init; }
}

/// <summary>
/// 原子替换某个主体的全部授予。
/// </summary>
public record ReplacePermissionGrantsInputDto
{
    /// <summary>
    /// 期望的当前版本。与服务端不一致时返回 409，避免两个管理员同时保存时后写覆盖前写。
    /// </summary>
    [Display(Name = "Expected revision")]
    public long ExpectedRevision { get; init; }

    [Display(Name = "Grants")]
    [MaxLength(500, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public IReadOnlyList<PermissionGrantInputDto> Grants { get; init; } = [];
}
#endif
