namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record CaptchaOutputDto
{
    public required string CaptchaToken { get; init; }
    public required string CaptchaImageBase64 { get; init; }
}
