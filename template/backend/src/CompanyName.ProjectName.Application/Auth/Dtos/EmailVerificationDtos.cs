using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>
/// 邮箱验证挑战的公开信息。
/// </summary>
public sealed record EmailVerificationChallengeOutputDto
{
    public required Guid ChallengeId { get; init; }

    public required int ExpiresInSeconds { get; init; }

    public required int RetryAfterSeconds { get; init; }
}

/// <summary>
/// 注册时提交的邮箱验证挑战应答。
/// </summary>
public sealed record EmailVerificationInputDto
{
    public required Guid ChallengeId { get; init; }

    [Required]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "The email verification code must contain exactly 6 digits.")]
    public required string Code { get; init; }
}
