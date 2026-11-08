namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>登录第二步的邮箱验证码已发出。</summary>
public sealed record TwoFactorEmailCodeOutputDto
{
    /// <summary>验证码的剩余有效期（秒）：新发的码是完整有效期，发送间隔内沿用的上一封是它的真实剩余；待完成的登录剩余不足一个完整有效期时不新发，所以码在有效期内总能用来完成登录。</summary>
    public required int ExpiresInSeconds { get; init; }

    /// <summary>多少秒后才能重发。</summary>
    public required int RetryAfterSeconds { get; init; }
}
