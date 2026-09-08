using System.Security.Cryptography;
using System.Text;
using Leistd.MultiTenancy.ConnectionStrings;
#if (LocalIdentity)
using Leistd.MultiTenancy;
#endif

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

public sealed record TenantMigrationTarget(Guid TenantId, string ConnectionString)
{
    public string Fingerprint => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(ConnectionString)));
}

/// <summary>
/// 枚举需要施加迁移的物理目标。
/// </summary>
/// <remarks>
/// 仅供 DbMigrator 一次性作业调用。配置无效时抛出 BCL 异常并终止作业；
/// 请求入口不直接调用此接口。
/// </remarks>
public interface ITenantMigrationTargetProvider
{
    Task<IReadOnlyList<TenantMigrationTarget>> GetDedicatedTargetsAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class TenantMigrationTargetProvider(
#if (LocalIdentity)
    ITenantConnectionConfigurationStore connectionStore,
#else
    IIdentityTenantConnectionClient identityClient,
#endif
    ISecretResolver secretResolver) : ITenantMigrationTargetProvider
{
    public async Task<IReadOnlyList<TenantMigrationTarget>> GetDedicatedTargetsAsync(
        CancellationToken cancellationToken = default)
    {
#if (LocalIdentity)
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
                throw new InvalidOperationException(
                    $"Tenant '{tenantId}' is configured for a dedicated database but has no migration " +
                    "Secret reference. Fix its connection configuration before running the migrator.");
            }

            var connectionString = await secretResolver.ResolveAsync(secretReference, cancellationToken);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"The migration Secret '{secretReference}' for tenant '{tenantId}' resolved to an " +
                    "empty value.");
            }

            targets.Add(new TenantMigrationTarget(tenantId, connectionString));
        }

        return targets;
    }
}
