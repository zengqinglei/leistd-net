namespace Leistd.ServiceClient.OAuth.Services;

/// <summary>
/// 服务访问令牌提供者：为具名客户端获取（并缓存）服务间调用的访问令牌。
/// </summary>
public interface IServiceTokenProvider
{
    /// <summary>
    /// 获取指定具名客户端的有效访问令牌；缓存未过期时直接返回，否则向 token 端点重新获取。
    /// </summary>
    /// <param name="clientName">具名客户端名（即 HttpClient / 配置节的服务名）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="Exceptions.ServiceClientException">token 端点不可达或返回错误</exception>
    Task<string> GetAccessTokenAsync(string clientName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使指定具名客户端的缓存令牌失效（下次获取将强制重取），用于收到 401 后自愈。
    /// </summary>
    /// <param name="clientName">具名客户端名</param>
    void Invalidate(string clientName);
}
