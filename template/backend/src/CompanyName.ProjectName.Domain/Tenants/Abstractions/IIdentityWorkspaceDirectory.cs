#if (TenantWorkspace && RemoteTokenAuth)
namespace CompanyName.ProjectName.Domain.Tenants.Abstractions;

public interface IIdentityWorkspaceDirectory
{
    Task<IReadOnlyList<IdentityWorkspace>> GetListAsync(CancellationToken cancellationToken = default);
}

public sealed record IdentityWorkspace
{
    public required Guid UserId { get; init; }
    public required Guid TenantId { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
}
#endif
