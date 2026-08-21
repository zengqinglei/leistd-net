namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record UserOutputDto
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
    public bool IsSuperAdmin { get; init; }
    public DateTime CreationTime { get; init; }
#if (LocalAuthorization)
    /// <summary>已分配角色名，仅用于展示；权限判断一律走 /api/v1/permissions/current。</summary>
    public required string[] Roles { get; init; }
#endif
}
