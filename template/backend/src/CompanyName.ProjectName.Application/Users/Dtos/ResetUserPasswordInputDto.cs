using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Users.Dtos;

/// <summary>
/// 重置用户密码输入 DTO
/// </summary>
public record ResetUserPasswordInputDto
{
    [Display(Name = "Password")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "{0} must be between {2} and {1} characters.")]
    [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
        ErrorMessage = "{0} must be at least 6 characters and contain both letters and numbers.")]
    public required string Password { get; init; }
}
