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
    /// 获取授权 URL
    /// </summary>
    string GetAuthorizationUrl(string redirectUri, string state);

    /// <summary>
    /// 使用 Authorization Code 换取 Token
    /// </summary>
    Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取第三方用户信息
    /// </summary>
    Task<ExternalUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default);
}
