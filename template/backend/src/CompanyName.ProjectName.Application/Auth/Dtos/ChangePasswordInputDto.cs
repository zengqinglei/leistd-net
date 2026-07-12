using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record ChangePasswordInputDto
{
    [Display(Name = "Current password")]
    [Required(ErrorMessage = "{0} is required.")]
    public required string CurrentPassword { get; init; }

    [Display(Name = "New password")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "{0} must be between {2} and {1} characters.")]
    [RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d).{6,}$",
        ErrorMessage = "{0} must be at least 6 characters and contain both letters and numbers.")]
    public required string NewPassword { get; init; }

    [Display(Name = "Confirm password")]
    [Required(ErrorMessage = "{0} is required.")]
    public required string ConfirmPassword { get; init; }
}
