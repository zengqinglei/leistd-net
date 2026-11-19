#if (TenantWorkspace && LocalIdentity)
using CompanyName.ProjectName.Application.Roles.Dtos;

namespace CompanyName.ProjectName.Application.Tenants.Dtos;

public sealed record TenantStaffOutputDto
{
    public required Guid HomeUserId { get; init; }
    public required Guid MemberId { get; init; }
    public required string Username { get; init; }
    /// <summary>租户内关联账号的用户名；首次授予时为 platform.&lt;Username&gt;，宿主改名后重新授予仍沿用原值。</summary>
    public required string LinkedUsername { get; init; }
    public string? DisplayName { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreationTime { get; init; }
    public required IReadOnlyList<RoleBriefOutputDto> Roles { get; init; }
}
#endif
