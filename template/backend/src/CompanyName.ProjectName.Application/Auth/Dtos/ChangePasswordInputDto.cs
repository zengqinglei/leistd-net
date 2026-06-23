using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record ChangePasswordInputDto
{
    [Display(Name = "当前密码")]
    [Required(ErrorMessage = "{0}不能为空")]
    public required string CurrentPassword { get; init; }

    [Display(Name = "新密码")]
    [Required(ErrorMessage = "{0}不能为空")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "{0}长度必须在 {2}-{1} 个字符之间")]
    [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
        ErrorMessage = "{0}需 6 位以上，包含字母和数字")]
    public required string NewPassword { get; init; }

    [Display(Name = "确认密码")]
    [Required(ErrorMessage = "{0}不能为空")]
    public required string ConfirmPassword { get; init; }
}
