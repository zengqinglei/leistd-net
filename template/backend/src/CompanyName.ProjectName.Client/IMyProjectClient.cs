using CompanyName.ProjectName.Client.Dtos;
using Refit;

namespace CompanyName.ProjectName.Client;

/// <summary>
/// 本服务的强类型调用客户端（Refit 接口，HTTP 实现由源生成器产出）。
/// 新增对外接口时在此补充方法与特性，并在 <c>Dtos/</c> 下补充对应 DTO。
/// </summary>
public interface IMyProjectClient
{
    /// <summary>
    /// 获取服务基础信息（匿名端点，可用于探活与联调）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    [Get("/api/v1/service-info")]
    Task<ServiceInfoDto> GetServiceInfoAsync(CancellationToken cancellationToken = default);

#if (LocalIdentity)
    /// <summary>
    /// 查询本次调用在被调方呈现的身份（用户 + 调用方客户端），用于服务间调用联调。
    /// 需要认证：服务间调用须配置 client credentials 并携带用户上下文。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    [Get("/api/v1/service-info/whoami")]
    Task<WhoAmIDto> WhoAmIAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按连接名查询指定租户的连接（机器端点）。
    /// </summary>
    /// <remarks>
    /// 连接名就是调用方 DbContext 的 <c>[ConnectionStringName]</c>；一次只回被问到的那一条。
    /// </remarks>
    [Get("/api/v1/tenant-connections/runtime/{tenantId}")]
    Task<TenantConnectionLookupDto> GetTenantConnectionAsync(
        Guid tenantId,
        [Query] string name,
        CancellationToken cancellationToken = default);

    /// <summary>按连接名枚举全部登记了连接的租户，供迁移作业使用。</summary>
    [Get("/api/v1/tenant-connections/migration")]
    Task<IReadOnlyList<TenantMigrationConnectionDto>> GetTenantMigrationConnectionsAsync(
        [Query] string name,
        CancellationToken cancellationToken = default);
#endif
}
