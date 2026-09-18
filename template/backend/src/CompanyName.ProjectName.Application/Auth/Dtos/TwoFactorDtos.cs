using System.ComponentModel.DataAnnotations;
using CompanyName.ProjectName.Domain.Shared.Text;
using CompanyName.ProjectName.Domain.Shared.Security.OneTimeCodes;

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

/// <summary>
/// 本人的两步验证状态
/// </summary>
public sealed record TwoFactorStatusOutputDto
{
    /// <summary>是否已启用。</summary>
    public bool Enabled { get; init; }

    /// <summary>剩余可用的恢复码个数。</summary>
    public int RecoveryCodesLeft { get; init; }

    /// <summary>所在租户要求启用两步验证（此时不能停用）。</summary>
    public bool RequiredByPolicy { get; init; }
}

/// <summary>
/// 开始设置两步验证：把密钥添加到身份验证器应用
/// </summary>
public sealed record TwoFactorSetupOutputDto
{
    /// <summary>Base32 密钥，供无法扫码时手动输入。</summary>
    public required string Secret { get; init; }

    /// <summary><c>otpauth://</c> 地址，界面据它生成二维码。</summary>
    public required string OtpAuthUri { get; init; }
}

/// <summary>
/// 提交一个验证码（启用、重新生成恢复码时用来证明手上确有身份验证器）
/// </summary>
public sealed record TwoFactorCodeInputDto
{
    /// <summary>身份验证器应用上的 6 位验证码。</summary>
    [Display(Name = "Verification code")]
    [Required(ErrorMessage = "{0} is required.")]
    [RegularExpression(@"^\s*\d{3}\s?\d{3}\s*$", ErrorMessage = "The verification code must contain exactly 6 digits.")]
    public required string Code { get; init; }
}

/// <summary>
/// 停用两步验证：密码与验证码都要
/// </summary>
public sealed record DisableTwoFactorInputDto
{
    /// <summary>当前密码。</summary>
    [Display(Name = "Password")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string Password { get; init; }

    /// <summary>身份验证器应用上的 6 位验证码。</summary>
    [Display(Name = "Verification code")]
    [Required(ErrorMessage = "{0} is required.")]
    [RegularExpression(@"^\s*\d{3}\s?\d{3}\s*$", ErrorMessage = "The verification code must contain exactly 6 digits.")]
    public required string Code { get; init; }
}

/// <summary>
/// 新生成的恢复码（明文只在这一次返回）
/// </summary>
public sealed record TwoFactorRecoveryCodesOutputDto
{
    /// <summary>恢复码明文。</summary>
    public required IReadOnlyList<string> RecoveryCodes { get; init; }
}
