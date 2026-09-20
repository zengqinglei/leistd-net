#if (LocalIdentity)
namespace CompanyName.ProjectName.Application.Settings.Dtos;

/// <summary>
/// 发一封测试邮件。
/// </summary>
/// <remarks>设置页的读写端点由设置组件提供（<c>MapSettings</c>），这里只剩发信测试这一项业务能力。</remarks>
public record SendTestEmailInputDto
{
    /// <summary>收件地址。</summary>
    [System.ComponentModel.DataAnnotations.Display(Name = "Recipient")]
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "{0} is required.")]
    [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "{0} is not a valid email address.")]
    [System.ComponentModel.DataAnnotations.StringLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string To { get; init; }
}
#endif
