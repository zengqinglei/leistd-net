#if (LocalIdentity)
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
#if (IncludeLocalization)
using Microsoft.Extensions.Localization;
#endif

namespace CompanyName.ProjectName.Application.OpenApplications.Dtos;

/// <summary>
/// 更新开放应用输入 DTO
/// </summary>
public record UpdateOpenApplicationInputDto : IValidatableObject
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
    [RegularExpression("^(web|native|service)$", ErrorMessage = "{0} is not an allowed value.")]
    public required string ApplicationType { get; init; }

    /// <summary>
    /// 客户端类型
    /// </summary>
    [Display(Name = "Client type")]
    [Required(ErrorMessage = "{0} is required.")]
    [RegularExpression("^(public|confidential)$", ErrorMessage = "{0} is not an allowed value.")]
    public required string ClientType { get; init; }

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

    /// <summary>
    /// 会话绑定：授权码与刷新令牌依赖签发时的 Identity 会话（见 <see cref="OpenApplicationSettings.SessionBound"/>）。
    /// 必须显式给出；浏览器 BFF 类客户端应为 <c>true</c>。
    /// </summary>
    [Display(Name = "Session bound")]
    [Required(ErrorMessage = "{0} is required.")]
    public required bool? SessionBound { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ValidateUris(RedirectUris, PostLogoutRedirectUris, validationContext);

    /// <summary>
    /// 回调地址逐项校验：绝对 URI、不含空白与片段。创建与更新共用这一份。
    /// </summary>
    /// <remarks>逐项校验没有对应的内置特性；自定义校验不经特性适配器，文案在这里按请求语言取；成员名也不经 JSON 命名策略转换，直接给请求体字段名。</remarks>
    internal static IEnumerable<ValidationResult> ValidateUris(
        IEnumerable<string> redirectUris,
        IEnumerable<string> postLogoutRedirectUris,
        ValidationContext validationContext)
    {
        const string message = "Each callback URI must be an absolute URI without whitespace or a fragment.";
#if (IncludeLocalization)
        var localizer = validationContext.GetService(typeof(IStringLocalizer)) as IStringLocalizer;
        var text = localizer?[message].Value ?? message;
#else
        var text = message;
#endif
        if (!redirectUris.All(IsValidUri))
            yield return new ValidationResult(text, [JsonNamingPolicy.CamelCase.ConvertName(nameof(RedirectUris))]);
        if (!postLogoutRedirectUris.All(IsValidUri))
            yield return new ValidationResult(text, [JsonNamingPolicy.CamelCase.ConvertName(nameof(PostLogoutRedirectUris))]);
    }

    private static bool IsValidUri(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && string.IsNullOrEmpty(uri.Fragment);
}
#endif
