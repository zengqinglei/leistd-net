namespace CompanyName.ProjectName.Application.Auth.Dtos;

public record SecurityConfigOutputDto
{
    public bool EnableEmailVerification { get; init; }
}
