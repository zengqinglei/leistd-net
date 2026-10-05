using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>
/// 登录第二步：提交验证码或恢复码（二选一）
/// </summary>
public sealed record TwoFactorLoginInputDto
{
    /// <summary>第一步返回的凭据。</summary>
    [Display(Name = "Two-factor token")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(128, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string Token { get; init; }

    /// <summary>身份验证器应用上的 6 位验证码。</summary>
    [Display(Name = "Verification code")]
    [RegularExpression(@"^\s*\d{3}\s?\d{3}\s*$", ErrorMessage = "The verification code must contain exactly 6 digits.")]
    public string? Code { get; init; }

    /// <summary>恢复码（手机不在身边时用）。</summary>
    [Display(Name = "Recovery code")]
    [StringLength(64, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? RecoveryCode { get; init; }
}
