#if (IncludeTenancy)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.Dtos;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

/// <summary>
/// 租户管理应用服务（宿主侧能力）
/// </summary>
public interface ITenantAppService
{
    /// <summary>分页查询租户</summary>
    Task<PagedResultDto<TenantOutputDto>> GetPagedAsync(GetTenantPagedInputDto input, CancellationToken cancellationToken = default);

    /// <summary>获取租户详情，不存在抛 404</summary>
    Task<TenantOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建租户并在租内种子初始角色、权限与管理员</summary>
    Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default);

    /// <summary>更新租户名称与显示名</summary>
    Task<TenantOutputDto> UpdateAsync(Guid id, UpdateTenantInputDto input, CancellationToken cancellationToken = default);

    /// <summary>启用/停用租户</summary>
    Task<TenantOutputDto> SetActivationAsync(Guid id, UpdateTenantActivationInputDto input, CancellationToken cancellationToken = default);

    /// <summary>删除租户（软删除，业务数据保留但不可达）</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>登录前按名称探测租户（匿名），不存在返回 null</summary>
    Task<TenantLookupOutputDto?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}
#endif
