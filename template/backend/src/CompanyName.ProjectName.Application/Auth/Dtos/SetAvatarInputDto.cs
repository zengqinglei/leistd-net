using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>
/// 设置自己的头像
/// </summary>
public record SetAvatarInputDto
{
    /// <summary>图片的 data URL（PNG / JPEG / WebP，浏览器端已裁剪缩放）；为空表示清除头像。</summary>
    [Display(Name = "Avatar")]
    public string? Avatar { get; init; }
}
