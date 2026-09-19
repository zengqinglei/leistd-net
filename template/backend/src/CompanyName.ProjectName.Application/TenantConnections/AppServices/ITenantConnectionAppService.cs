#if (LocalIdentity)
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;

namespace CompanyName.ProjectName.Application.TenantConnections.AppServices;

public interface ITenantConnectionAppService : IAppService
{
    /// <summary>列出该租户已登记的全部连接（不含连接串）。列表为空即该租户不单独分库。</summary>
    Task<IReadOnlyList<TenantConnectionOutputDto>> GetListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>按连接名下发运行时连接，供资源服务解析路由。</summary>
    Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>按连接名枚举全部登记了连接的租户，供迁移作业使用。</summary>
    Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>登记或更新一条连接。</summary>
    Task<TenantConnectionOutputDto> SetAsync(
        Guid tenantId,
        string name,
        UpsertTenantConnectionInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>删除一条连接，该名字之后回落到服务自己的配置。</summary>
    Task RemoveAsync(
        Guid tenantId,
        string name,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
#endif
