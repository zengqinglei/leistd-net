#if (RemoteTokenAuth)
using CompanyName.ProjectName.Domain.Systems.Abstractions;
using CompanyName.ProjectName.Application.Systems.Dtos;
using Leistd.Ddd.Application.AppServices;
using Leistd.ObjectMapping.Abstractions;

namespace CompanyName.ProjectName.Application.Systems.AppServices;

public sealed class RemoteSystemAppService(IIdentitySystemDirectory directory, IObjectMapper mapper) : BaseAppService, ISystemAppService
{
    public async Task<IReadOnlyList<SystemOutputDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        (await directory.GetListAsync(cancellationToken)).Select(entry => mapper.Map<SystemDirectoryEntry, SystemOutputDto>(entry)).ToArray();
}
#endif
