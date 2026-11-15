#if (TenantWorkspace)
namespace CompanyName.ProjectName.Application.Tenants.Dtos;

public sealed record WorkspaceOutputDto
{
    public required Guid TenantId { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
}
#endif
