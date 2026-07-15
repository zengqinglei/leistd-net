using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record SendEmailCodeInputDto
{
    [Display(Name = "Email")]
    [Required(ErrorMessage = "{0} is required.")]
    [EmailAddress(ErrorMessage = "{0} has an invalid format.")]
    public required string Email { get; init; }

    [Display(Name = "Captcha token")]
    [Required(ErrorMessage = "The captcha has expired. Refresh it and try again.")]
    public required string CaptchaToken { get; init; }

    [Display(Name = "Captcha")]
    [Required(ErrorMessage = "{0} is required.")]
    public required string CaptchaCode { get; init; }
}
