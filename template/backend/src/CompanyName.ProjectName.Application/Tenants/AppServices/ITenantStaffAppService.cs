#if (TenantWorkspace && LocalIdentity)
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

public interface ITenantStaffAppService : IAppService
{
    Task<IReadOnlyList<TenantStaffOutputDto>> GetListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleBriefOutputDto>> GetRolesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantStaffCandidateOutputDto>> GetCandidatesAsync(Guid tenantId, string? keyword = null, CancellationToken cancellationToken = default);
    Task GrantAsync(Guid tenantId, GrantTenantStaffInputDto input, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid tenantId, Guid homeUserId, CancellationToken cancellationToken = default);
}
#endif
