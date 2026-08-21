using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Users.Dtos;

/// <summary>
/// 创建用户输入 DTO
/// </summary>
public record CreateUserInputDto
{
#if (ResourceService)
    /// <summary>Identity 签发的稳定 sub，也是本服务 Membership 主键。</summary>
    public required Guid SubjectId { get; init; }

#endif
    [Display(Name = "Username")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(64, MinimumLength = 3, ErrorMessage = "{0} must be between {2} and {1} characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "{0} can contain only letters, numbers, and underscores.")]
    public required string Username { get; init; }

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

#if (IdentityService)
    [Display(Name = "Password")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "{0} must be between {2} and {1} characters.")]
    [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
        ErrorMessage = "{0} must be at least 6 characters and contain both letters and numbers.")]
    public required string Password { get; init; }
#endif

    public bool IsActive { get; init; } = true;

#if (IdentityService)
    public bool IsEmailVerified { get; init; }
#endif

#if (LocalAuthorization)
    /// <summary>
    /// 初始角色 Id 集合。按 Id 提交而非角色名，角色名只用于展示。
    /// </summary>
    /// <remarks>
    /// 非空时应用服务会额外要求 <c>App.Users.ManageRoles</c>：
    /// 只持有创建权限的主体不能在创建时把自己或他人提升为管理员。
    /// </remarks>
    [Display(Name = "Roles")]
    [MaxLength(20, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public List<Guid> RoleIds { get; init; } = [];
#endif
}
