using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Users.Dtos;

/// <summary>
/// 更新用户输入 DTO
/// </summary>
/// <remarks>
/// 不包含角色字段：角色分配是独立的命令，走 <c>PUT /api/v1/users/{id}/roles</c>
/// 并要求 <c>App.Users.ManageRoles</c>。否则任何持有用户编辑权限的主体都能提权。
/// </remarks>
public record UpdateUserInputDto
{
    [Display(Name = "Email")]
    [Required(ErrorMessage = "{0} is required.")]
    [EmailAddress(ErrorMessage = "{0} has an invalid format.")]
    [StringLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string Email { get; init; }

    [Display(Name = "Display name")]
    [StringLength(128, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? DisplayName { get; init; }

    [Display(Name = "Avatar")]
    [StringLength(1500000, ErrorMessage = "{0} is too large. Compress it and try again.")]
    public string? Avatar { get; init; }

    public bool IsActive { get; init; }

    public bool IsEmailVerified { get; init; }
}
