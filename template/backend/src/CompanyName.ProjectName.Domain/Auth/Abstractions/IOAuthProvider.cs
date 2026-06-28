namespace CompanyName.ProjectName.Domain.Auth.Abstractions;

/// <summary>
/// OAuth 提供商服务基础接口
/// </summary>
public interface IOAuthProvider
{
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

/// <summary>
/// OAuth Token 信息
/// </summary>
public record OAuthTokenInfo
{
    public required string AccessToken { get; init; }
    public string? TokenType { get; init; }
    public int? ExpiresIn { get; init; }
    public string? RefreshToken { get; init; }
    public string? Scope { get; init; }
}

/// <summary>
/// 外部用户信息
/// </summary>
public record ExternalUserInfo
{
    public required string ProviderId { get; init; }
    public string? Email { get; init; }
    public required string Username { get; init; }
    public string? Nickname { get; init; }
    public string? AvatarUrl { get; init; }
}
