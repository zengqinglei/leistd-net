#if (TenantWorkspace && LocalIdentity)
namespace CompanyName.ProjectName.Application.Tenants.Dtos;

/// <summary>授予入口所需的宿主账号目录。</summary>
public sealed record TenantStaffCandidateOutputDto(Guid Id, string Username, string? DisplayName);
#endif
