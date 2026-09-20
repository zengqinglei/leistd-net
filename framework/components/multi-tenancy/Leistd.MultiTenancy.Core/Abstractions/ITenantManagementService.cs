using Leistd.Data.Paging;
using Leistd.MultiTenancy.Dtos;

namespace Leistd.MultiTenancy.Abstractions;

/// <summary>
/// 租户管理用例（宿主侧）：查询、创建编排与补偿、更新、启停、删除，以及登录前按名称探测。
/// </summary>
/// <remarks>
/// <para><b>创建的顺序是硬的</b>：先以停用态登记租户，同一控制面工作单元里登记连接（分库在开通之前定案）；
/// 再在新租户上下文与新工作单元里经 <see cref="Provisioning.ITenantProvisioner"/> 开通；最后启用。
/// 任一步失败按固定顺序补偿：清开通数据 → 删连接登记 → 删租户（删连接必须在删租户之前，
/// 否则已删租户的连接行再也删不掉）。</para>
/// <para>不做权限检查——由端点策略决定。成功后发布 <see cref="Events.TenantChangedEvent"/>。</para>
/// </remarks>
public interface ITenantManagementService
{
    /// <summary>按关键字分页查询租户，按创建时间倒序。</summary>
    /// <param name="input">关键字与分页。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PagedResult<TenantOutputDto>> GetPagedAsync(GetTenantPagedInputDto input, CancellationToken cancellationToken = default);

    /// <summary>获取租户详情。</summary>
    /// <param name="id">租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="Exceptions.TenantNotFoundException">租户不存在或已删除。</exception>
    Task<TenantOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建租户并开通，完成后启用。</summary>
    /// <param name="input">创建入参；宿主可传派生类型携带开通所需的更多信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default);

    /// <summary>更新名称、显示名与描述。</summary>
    /// <param name="id">租户标识。</param>
    /// <param name="input">新值；全部覆盖。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantOutputDto> UpdateAsync(Guid id, UpdateTenantInputDto input, CancellationToken cancellationToken = default);

    /// <summary>启用或停用；启用前依次经过宿主注册的 <see cref="Provisioning.ITenantActivationGuard"/>。</summary>
    /// <param name="id">租户标识。</param>
    /// <param name="input">目标状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantOutputDto> SetActivationAsync(Guid id, UpdateTenantActivationInputDto input, CancellationToken cancellationToken = default);

    /// <summary>软删除租户，业务数据保留但不可达。</summary>
    /// <param name="id">租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>登录前按名称探测租户；不存在返回 <see langword="null"/>。</summary>
    /// <param name="name">租户名称，大小写不敏感。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantLookupOutputDto?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}
