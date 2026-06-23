using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Users.Dtos;

/// <summary>
/// 重置用户密码输入 DTO
/// </summary>
public record ResetUserPasswordInputDto
{
    [Display(Name = "密码")]
    [Required(ErrorMessage = "{0}不能为空")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "{0}长度必须在 {2}-{1} 个字符之间")]
    [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
        ErrorMessage = "{0}需 6 位以上，包含字母和数字")]
    public required string Password { get; init; }
}
