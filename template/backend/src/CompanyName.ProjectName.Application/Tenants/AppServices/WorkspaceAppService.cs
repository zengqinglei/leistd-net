#if (TenantWorkspace && LocalIdentity)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Tenants.Entities;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.Context;
using Leistd.Security.Users;
using Leistd.UnitOfWork;
using CompanyName.ProjectName.Domain.Users.Repositories;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

public sealed class WorkspaceAppService(IRepository<UserTenantAccess, Guid> accessRepository,
    ITenantStore tenantStore, ICurrentUser currentUser, IUserRepository userRepository,
    ICurrentTenant currentTenant, IUnitOfWorkManager unitOfWorkManager) : BaseAppService, IWorkspaceAppService
{
    public async Task<IReadOnlyList<WorkspaceOutputDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } homeId) return [];
        if (currentUser.TenantId is { } ownId)
        {
            var own = await tenantStore.FindAsync(ownId, cancellationToken);
            return own is { IsActive: true } ? [new() { UserId = homeId, TenantId = own.Id, Name = own.Name, DisplayName = own.DisplayName }] : [];
        }
        var accesses = await accessRepository.GetListAsync(a => a.HomeUserId == homeId, cancellationToken);
        var result = new List<WorkspaceOutputDto>();
        foreach (var access in accesses)
        {
            var tenant = await tenantStore.FindAsync(access.TenantId, cancellationToken);
            if (tenant is not { IsActive: true }) continue;
            using (currentTenant.Change(tenant.Id, tenant.Name))
            using (unitOfWorkManager.Begin(requiresNew: true))
            {
                if (await userRepository.GetOneAsync(u => u.HomeUserId == homeId && u.IsActive, cancellationToken) is { } member)
                    result.Add(new() { UserId = member.Id, TenantId = tenant.Id, Name = tenant.Name, DisplayName = tenant.DisplayName });
            }
        }
        return result.OrderBy(w => w.Name).ToList();
    }
}
#endif
