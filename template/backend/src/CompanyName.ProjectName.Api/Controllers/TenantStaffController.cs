#if (TenantWorkspace && LocalIdentity)
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Tenants.AppServices;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

[Authorize(Policy = PermissionConstant.Tenants.StaffAccess)]
[Route("api/v1/tenants/{tenantId:guid}/staff")]
public sealed class TenantStaffController(ITenantStaffAppService staffAppService) : BaseController
{
    [HttpGet]
    public Task<IReadOnlyList<TenantStaffOutputDto>> GetListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        staffAppService.GetListAsync(tenantId, cancellationToken);

    [HttpGet("roles")]
    public Task<IReadOnlyList<RoleBriefOutputDto>> GetRolesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        staffAppService.GetRolesAsync(tenantId, cancellationToken);

    [HttpGet("candidates")]
    public Task<IReadOnlyList<TenantStaffCandidateOutputDto>> GetCandidatesAsync(Guid tenantId, [FromQuery] string? keyword, CancellationToken cancellationToken) =>
        staffAppService.GetCandidatesAsync(tenantId, keyword, cancellationToken);

    [HttpPost]
    public Task GrantAsync(Guid tenantId, GrantTenantStaffInputDto input, CancellationToken cancellationToken) =>
        staffAppService.GrantAsync(tenantId, input, cancellationToken);

    [HttpDelete("{homeUserId:guid}")]
    public Task RemoveAsync(Guid tenantId, Guid homeUserId, CancellationToken cancellationToken) =>
        staffAppService.RemoveAsync(tenantId, homeUserId, cancellationToken);
}
#endif
