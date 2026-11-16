#if (OpenIddictServer || RemoteTokenAuth)
using CompanyName.ProjectName.Application.Systems.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Systems.AppServices;

public interface ISystemAppService : IAppService
{
    Task<IReadOnlyList<SystemOutputDto>> GetListAsync(CancellationToken cancellationToken = default);
}
#endif
