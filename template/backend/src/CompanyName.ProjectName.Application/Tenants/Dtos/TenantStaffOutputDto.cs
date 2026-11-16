#if (TenantWorkspace && LocalIdentity)
using CompanyName.ProjectName.Application.Roles.Dtos;

namespace CompanyName.ProjectName.Application.Tenants.Dtos;

public sealed record TenantStaffOutputDto
{
    public required Guid HomeUserId { get; init; }
    public required Guid MemberId { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreationTime { get; init; }
    public required IReadOnlyList<RoleBriefOutputDto> Roles { get; init; }
}
#endif
