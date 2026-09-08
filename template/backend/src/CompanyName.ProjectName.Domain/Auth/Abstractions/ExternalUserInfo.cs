namespace CompanyName.ProjectName.Domain.Auth.Abstractions;

/// <summary>
/// 外部用户信息
/// </summary>
public record ExternalUserInfo
{
    public required string ProviderId { get; init; }
    public string? Email { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
}
