using CompanyName.ProjectName.Client.Dtos;

namespace CompanyName.ProjectName.Client;

/// <summary>
/// 本服务的强类型调用客户端。
/// </summary>
public interface IMyProjectClient
{
    /// <summary>
    /// 获取服务基础信息（匿名端点，可用于探活与联调）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<ServiceInfoDto?> GetServiceInfoAsync(CancellationToken cancellationToken = default);

#if (IncludeIdentity)
    /// <summary>
    /// 查询本次调用在被调方呈现的身份（用户 + 调用方客户端），用于服务间调用联调。
    /// 需要认证：服务间调用须配置 client credentials 并携带用户上下文。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<WhoAmIDto?> WhoAmIAsync(CancellationToken cancellationToken = default);
#endif
}
