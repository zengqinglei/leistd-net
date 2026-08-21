#if (IdentityService)
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

internal sealed class LocalTenantConnectionStringResolver(
    ICurrentTenant currentTenant,
    IdentityControlDbContext controlDbContext,
    ISecretResolver secretResolver,
    IConfiguration configuration) : ITenantConnectionStringResolver
{
    public async Task<string> ResolveAsync(
        string connectionStringName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        if (string.Equals(
                connectionStringName,
                IdentityControlDbContext.ConnectionStringName,
                StringComparison.Ordinal))
        {
            return RequireConnection(
                configuration.GetConnectionString(IdentityControlDbContext.ConnectionStringName)
                ?? configuration.GetConnectionString("Default"));
        }

        var defaultConnection = configuration.GetConnectionString(connectionStringName);
        if (!currentTenant.IsAvailable)
        {
            return RequireConnection(defaultConnection);
        }

        var tenantId = currentTenant.Id!.Value;
        var record = await controlDbContext.Set<TenantConnectionRecord>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Join(
                controlDbContext.Set<TenantRecord>().AsNoTracking().Where(x => !x.IsDeleted),
                connection => connection.TenantId,
                tenant => tenant.Id,
                (connection, _) => connection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The tenant connection configuration does not exist.");

        return record.DatabaseMode switch
        {
            TenantDatabaseMode.SharedDatabase => RequireConnection(defaultConnection),
            TenantDatabaseMode.DedicatedDatabase when !string.IsNullOrWhiteSpace(record.RuntimeSecretReference) =>
                RequireConnection(await secretResolver.ResolveAsync(record.RuntimeSecretReference, cancellationToken)),
            _ => throw new InvalidOperationException("The tenant connection configuration is invalid.")
        };
    }

    private static string RequireConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The required connection string is not configured.");
        }

        return connectionString;
    }
}
#endif
