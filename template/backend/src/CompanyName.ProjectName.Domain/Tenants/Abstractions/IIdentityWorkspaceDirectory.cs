#if (TenantWorkspace && RemoteTokenAuth)
using Leistd.MultiTenancy.Stores;

namespace CompanyName.ProjectName.Domain.Tenants.Abstractions;

public interface IIdentityWorkspaceDirectory
{
    Task<TenantConfiguration?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default);
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
