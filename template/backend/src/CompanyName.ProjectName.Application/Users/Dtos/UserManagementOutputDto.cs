#if (LocalAuthorization)
using CompanyName.ProjectName.Application.Roles.Dtos;
#endif

namespace CompanyName.ProjectName.Application.Users.Dtos;

/// <summary>
/// 用户管理输出 DTO
/// </summary>
public record UserManagementOutputDto
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public bool IsActive { get; init; }
#if (IdentityService)
    public bool IsEmailVerified { get; init; }
#endif
#if (LocalAuthorization)
    /// <summary>
    /// 已分配角色。携带 Id 供提交使用，Name 与 DisplayName 只用于展示。
    /// </summary>
    public required IReadOnlyList<RoleBriefDto> Roles { get; init; }
#endif
    public bool IsSuperAdmin { get; init; }
    public DateTime CreationTime { get; init; }
#if (IdentityService)
    public DateTime? LastLoginTime { get; init; }
#endif
}
