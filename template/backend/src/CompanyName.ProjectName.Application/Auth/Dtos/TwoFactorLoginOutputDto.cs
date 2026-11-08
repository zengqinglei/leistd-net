using CompanyName.ProjectName.Domain.Users.ValueObjects;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>待完成的登录（第一步已通过、尚待第二步）：第二步页刷新或经授权端点进入时据此继续。</summary>
public sealed record TwoFactorLoginOutputDto
{
    /// <summary>本人此刻能用的方式，按系统顺序，恢复码在最后。</summary>
    public required IReadOnlyList<TwoFactorMethod> TwoFactorMethods { get; init; }

    /// <summary>直接给出的方式（本人首选，否则按系统顺序）。</summary>
    public required TwoFactorMethod DefaultTwoFactorMethod { get; init; }

    /// <summary>邮箱验证码能用时，收码地址的打码形式（如 <c>j***@example.com</c>）。</summary>
    public string? MaskedEmail { get; init; }
}
