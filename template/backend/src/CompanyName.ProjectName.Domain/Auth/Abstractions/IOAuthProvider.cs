namespace CompanyName.ProjectName.Domain.Auth.Abstractions;

/// <summary>
/// OAuth 提供商服务基础接口
/// </summary>
public interface IOAuthProvider
{
    /// <summary>
    /// 提供商标识
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否已完整配置并可对外提供服务。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 获取授权 URL。
    /// </summary>
    /// <remarks>客户端标识与回调地址由提供商实现从部署配置中读取。</remarks>
    string GetAuthorizationUrl(string state);

    /// <summary>
    /// 使用 Authorization Code 换取 Token
    /// </summary>
    Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取第三方用户信息
    /// </summary>
    Task<ExternalUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default);
}
