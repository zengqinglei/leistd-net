#if (IncludeRoles)
using System.ComponentModel.DataAnnotations;
using Leistd.Ddd.Application.Contracts.Dtos;

namespace CompanyName.ProjectName.Application.Roles.Dtos;

/// <summary>
/// 角色输出 DTO
/// </summary>
public record RoleOutputDto
{
    public required Guid Id { get; init; }

    /// <summary>角色名称，唯一且创建后不可修改，作为稳定的业务标识。</summary>
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    /// <summary>系统内置角色，不可删除。</summary>
    public bool IsStatic { get; init; }

    /// <summary>默认角色，新用户自动分配。</summary>
    public bool IsDefault { get; init; }

    public int Sort { get; init; }

    /// <summary>关联用户数。</summary>
    public int UserCount { get; init; }

    /// <summary>已授予的权限数。</summary>
    public int PermissionCount { get; init; }

    public DateTime CreationTime { get; init; }

    public DateTime? LastModificationTime { get; init; }
}

/// <summary>
/// 角色简要信息，用于用户列表与下拉选择。
/// </summary>
public record RoleBriefDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// 获取角色分页列表输入 DTO
/// </summary>
public record GetRolePagedInputDto : PagedRequestDto
{
    /// <summary>
    /// 搜索关键字（名称、显示名称）
    /// </summary>
    [Display(Name = "Search keyword")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }
}

/// <summary>
/// 创建角色输入 DTO
/// </summary>
public record CreateRoleInputDto
{
    [Display(Name = "Role name")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(64, MinimumLength = 2, ErrorMessage = "{0} must be between {2} and {1} characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "{0} can contain only letters, numbers, and underscores.")]
    public required string Name { get; init; }

    [Display(Name = "Display name")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(128, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string DisplayName { get; init; }

    [Display(Name = "Description")]
    [StringLength(512, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Description { get; init; }

    [Display(Name = "Sort")]
    [Range(0, 9999, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int Sort { get; init; }

    public bool IsDefault { get; init; }
}

/// <summary>
/// 更新角色输入 DTO。角色名称是稳定业务标识，创建后不可修改。
/// </summary>
public record UpdateRoleInputDto
{
    [Display(Name = "Display name")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(128, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string DisplayName { get; init; }

    [Display(Name = "Description")]
    [StringLength(512, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Description { get; init; }

    [Display(Name = "Sort")]
    [Range(0, 9999, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int Sort { get; init; }

    public bool IsDefault { get; init; }
}
#endif
