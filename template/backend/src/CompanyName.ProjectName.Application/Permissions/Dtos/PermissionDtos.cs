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
    /// <summary>已生效的权限名集合（用户授予与各角色授予的并集）。</summary>
    [Display(Name = "Permissions")]
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>是否超级管理员（旁路全部功能权限）。</summary>
    [Display(Name = "Super administrator")]
    public bool IsSuperAdmin { get; init; }

    /// <summary>有效权限的版本标记。变化即表示本地缓存已过期。</summary>
    [Display(Name = "Version token")]
    public required string VersionToken { get; init; }
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
/// <remarks>
/// 授予是纯加法，只有"已授予"与"未授予"两种；角色没有上游来源，因此不存在"继承"一说。
/// </remarks>
public record PermissionGrantStateDto
{
    [Display(Name = "Permission name")]
    public required string Name { get; init; }

    /// <summary>该角色是否已被授予该权限。</summary>
    [Display(Name = "Granted")]
    public bool Granted { get; init; }
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
    [Display(Name = "Version")]
    public long Version { get; init; }

    [Display(Name = "Grants")]
    public required IReadOnlyList<PermissionGrantStateDto> Grants { get; init; }
}

/// <summary>
/// 原子替换某个主体的全部授予。
/// </summary>
public record ReplacePermissionGrantsInputDto
{
    /// <summary>
    /// 期望的当前版本。与服务端不一致时返回 409，避免两个管理员同时保存时后写覆盖前写。
    /// </summary>
    [Display(Name = "Expected version")]
    public long ExpectedVersion { get; init; }

    /// <summary>目标权限名集合，未出现的权限视为撤销。</summary>
    [Display(Name = "Permissions")]
    [MaxLength(500, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public IReadOnlyList<string> PermissionNames { get; init; } = [];
}
