#if (TenantWorkspace && RemoteTokenAuth)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Tenants.Abstractions;
using Leistd.Ddd.Application.AppServices;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

public sealed class RemoteWorkspaceAppService(IIdentityWorkspaceDirectory directory) : BaseAppService, IWorkspaceAppService
{
    public async Task<IReadOnlyList<WorkspaceOutputDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        (await directory.GetListAsync(cancellationToken)).Select(w => new WorkspaceOutputDto
        { UserId = w.UserId, TenantId = w.TenantId, Name = w.Name, DisplayName = w.DisplayName }).ToList();
}
#endif
