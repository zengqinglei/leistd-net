#if (OpenIddictServer)
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Repositories;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.Ddd.Application.AppServices;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Exceptions;
#if (IncludeMultiTenancy)
using Leistd.MultiTenancy.Stores;
#endif
using Leistd.Timing;
using Leistd.UnitOfWork;

namespace CompanyName.ProjectName.Application.Users.AppServices;

/// <inheritdoc cref="IAdministratorAppService"/>
internal sealed class AdministratorAppService(
    ICurrentTenant currentTenant,
#if (IncludeMultiTenancy)
    ITenantStore tenantStore,
#endif
    IUnitOfWorkManager unitOfWorkManager,
    IUserRepository userRepository,
    RoleDomainService roleDomainService,
    IClock clock) : BaseAppService, IAdministratorAppService
{
    public async Task<AdministratorStatusOutputDto> GetForTenantUserAsync(
        string tenant, Guid userId, CancellationToken cancellationToken = default)
    {
        Guid? tenantId;
        string? tenantName = null;
        if (string.Equals(tenant, "host", StringComparison.Ordinal))
        {
            tenantId = null;
        }
#if (IncludeMultiTenancy)
        else if (Guid.TryParse(tenant, out var id)
                 && await tenantStore.FindAsync(id, cancellationToken) is { IsActive: true } record)
        {
            tenantId = id;
            tenantName = record.Name;
        }
#endif
        else
        {
            throw new TenantNotFoundException(tenant);
        }

        // 在目标租户内新开工作单元，使用该租户的路由及数据过滤器。
        using var tenantScope = currentTenant.Change(tenantId, tenantName);
        using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true);
        var user = await userRepository.GetWithRolesAsync(userId, cancellationToken);
        if (user is null || user.GetAccessStatus(clock.Now) != UserAccessStatus.Allowed)
        {
            return new AdministratorStatusOutputDto { IsAdministrator = false };
        }

        if (!tenantId.HasValue && user.IsSuperAdmin)
        {
            return new AdministratorStatusOutputDto { IsAdministrator = true };
        }

        var admin = await roleDomainService.FindBuiltInAdminAsync(cancellationToken);
        return new AdministratorStatusOutputDto { IsAdministrator = admin is not null && user.IsInRole(admin.Id) };
    }
}
#endif
