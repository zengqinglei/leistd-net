#if (TenantWorkspace && LocalIdentity)
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

public interface ITenantStaffAppService : IAppService
{
    Task<IReadOnlyList<TenantStaffOutputDto>> GetListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleBriefOutputDto>> GetRolesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    /// <summary>按关键字（用户名或显示名包含匹配）查可授予的宿主用户，最多返回 <c>TenantStaffAppService.CandidateLimit</c> 条。</summary>
    /// <remarks>候选列表不分页：返回满额时由界面提示继续输入缩小范围。</remarks>
    Task<IReadOnlyList<TenantStaffCandidateOutputDto>> GetCandidatesAsync(Guid tenantId, string? keyword = null, CancellationToken cancellationToken = default);
    Task GrantAsync(Guid tenantId, GrantTenantStaffInputDto input, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid tenantId, Guid homeUserId, CancellationToken cancellationToken = default);
}
#endif
