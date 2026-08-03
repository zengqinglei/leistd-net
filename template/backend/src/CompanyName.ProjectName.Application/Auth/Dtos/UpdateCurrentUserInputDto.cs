using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record UpdateCurrentUserInputDto
{
    [Display(Name = "Username")]
    [Required(ErrorMessage = "{0} is required.")]
    [StringLength(64, MinimumLength = 3, ErrorMessage = "{0} must be between {2} and {1} characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "{0} can contain only letters, numbers, and underscores.")]
    public required string Username { get; init; }

    [Display(Name = "Email")]
    [Required(ErrorMessage = "{0} is required.")]
    [EmailAddress(ErrorMessage = "{0} has an invalid format.")]
    [StringLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string Email { get; init; }

    [Display(Name = "DisplayName")]
    [StringLength(128, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? DisplayName { get; init; }

    [Display(Name = "Phone number")]
    [MaxLength(20, ErrorMessage = "{0} cannot exceed {1} characters.")]
    [RegularExpression(@"^[0-9+\-()\s]{0,20}$", ErrorMessage = "{0} has an invalid format.")]
    public string? PhoneNumber { get; init; }

    [Display(Name = "Avatar")]
    [StringLength(1500000, ErrorMessage = "{0} is too large. Compress it and try again.")]
    public string? Avatar { get; init; }
}
