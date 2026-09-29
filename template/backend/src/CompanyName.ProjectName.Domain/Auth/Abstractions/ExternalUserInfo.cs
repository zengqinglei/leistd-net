namespace CompanyName.ProjectName.Domain.Auth.Abstractions;

/// <summary>
/// 外部用户信息
/// </summary>
public record ExternalUserInfo
{
    public required string ProviderId { get; init; }
    public string? Email { get; init; }

    /// <summary>提供商确认 <see cref="Email"/> 属于该外部账号。只有为真时才可能按邮箱关联已有用户。</summary>
    public bool EmailVerified { get; init; }

    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
}
