namespace CompanyName.ProjectName.Domain.Auth.Abstractions;

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
