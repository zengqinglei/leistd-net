#if (TenantWorkspace)
using CompanyName.ProjectName.Application.Tenants.AppServices;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

[Authorize]
[Route("api/v1/auth/me/workspaces")]
public sealed class WorkspaceController(IWorkspaceAppService workspaceAppService) : BaseController
{
    [HttpGet]
    public Task<IReadOnlyList<WorkspaceOutputDto>> GetListAsync(CancellationToken cancellationToken) =>
        workspaceAppService.GetListAsync(cancellationToken);
}
#endif
