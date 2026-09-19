using System.ComponentModel.DataAnnotations;
using CompanyName.ProjectName.Domain.Shared.Text;
using CompanyName.ProjectName.Domain.Shared.Security.OneTimeCodes;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>
/// 登录第一步（密码或外部登录）的结果
/// </summary>
public sealed record SessionLoginOutputDto
{
    /// <summary>还需要第二步：凭 <see cref="TwoFactorToken"/> 提交验证码或恢复码后才会下发会话。</summary>
    public bool RequiresTwoFactor { get; init; }

    /// <summary>第二步凭据，几分钟内有效；不需要第二步时为 null。</summary>
    public string? TwoFactorToken { get; init; }
}
