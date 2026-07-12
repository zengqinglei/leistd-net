#if (IncludeIdentity)
using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.OpenApplications.Dtos;

/// <summary>
/// 更新开放应用输入 DTO
/// </summary>
public record UpdateOpenApplicationInputDto
{
    /// <summary>
    /// 显示名称
    /// </summary>
    [Display(Name = "Display name")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? DisplayName { get; init; }

    /// <summary>
    /// 应用类型
    /// </summary>
    [Display(Name = "Application type")]
    [Required(ErrorMessage = "{0} is required.")]
    public required string ApplicationType { get; init; }

    /// <summary>
    /// 客户端类型
    /// </summary>
    [Display(Name = "Client type")]
    [Required(ErrorMessage = "{0} is required.")]
    public required string ClientType { get; init; }

    /// <summary>
    /// 同意类型
    /// </summary>
    [Display(Name = "Consent type")]
    [Required(ErrorMessage = "{0} is required.")]
    public required string ConsentType { get; init; }

    /// <summary>
    /// Redirect URIs
    /// </summary>
    public List<string> RedirectUris { get; init; } = [];

    /// <summary>
    /// Post Logout Redirect URIs
    /// </summary>
    public List<string> PostLogoutRedirectUris { get; init; } = [];

    /// <summary>
    /// 授权能力
    /// </summary>
    public List<string> Permissions { get; init; } = [];

    /// <summary>
    /// 要求
    /// </summary>
    public List<string> Requirements { get; init; } = [];
}
#endif
