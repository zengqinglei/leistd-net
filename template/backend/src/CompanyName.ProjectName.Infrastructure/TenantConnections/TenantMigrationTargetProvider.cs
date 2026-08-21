using System.Security.Cryptography;
using System.Text;
#if (IdentityService)
using Leistd.MultiTenancy;
#endif

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

public sealed record TenantMigrationTarget(Guid TenantId, string ConnectionString)
{
    public string Fingerprint => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(ConnectionString)));
}

public interface ITenantMigrationTargetProvider
{
    Task<IReadOnlyList<TenantMigrationTarget>> GetDedicatedTargetsAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class TenantMigrationTargetProvider(
#if (IdentityService)
    ITenantConnectionConfigurationStore connectionStore,
#else
    IIdentityTenantConnectionClient identityClient,
#endif
    ISecretResolver secretResolver) : ITenantMigrationTargetProvider
{
    public async Task<IReadOnlyList<TenantMigrationTarget>> GetDedicatedTargetsAsync(
        CancellationToken cancellationToken = default)
    {
#if (IdentityService)
        var configurations = await connectionStore.GetListAsync(cancellationToken);
        var dedicated = configurations
            .Where(x => x.DatabaseMode == TenantDatabaseMode.DedicatedDatabase)
            .Select(x => (x.TenantId, x.MigrationSecretReference));
#else
        var configurations = await identityClient.GetMigrationListAsync(cancellationToken);
        var dedicated = configurations
            .Where(x => x.DatabaseMode == RemoteTenantDatabaseMode.DedicatedDatabase)
            .Select(x => (x.TenantId, x.MigrationSecretReference));
#endif

        var targets = new List<TenantMigrationTarget>();
        foreach (var (tenantId, secretReference) in dedicated)
        {
            if (string.IsNullOrWhiteSpace(secretReference))
            {
                throw new InvalidOperationException("A dedicated tenant is missing its migration Secret reference.");
            }

            var connectionString = await secretResolver.ResolveAsync(secretReference, cancellationToken);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("A migration Secret resolved to an empty value.");
            }

            targets.Add(new TenantMigrationTarget(tenantId, connectionString));
        }

        return targets;
    }
}
