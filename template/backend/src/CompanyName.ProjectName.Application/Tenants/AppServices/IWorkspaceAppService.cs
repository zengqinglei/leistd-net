#if (TenantWorkspace)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

public interface IWorkspaceAppService : IAppService
{
    Task<IReadOnlyList<WorkspaceOutputDto>> GetListAsync(CancellationToken cancellationToken = default);
}
#endif
