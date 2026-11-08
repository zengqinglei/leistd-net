using System.Reflection;
using Leistd.Data.Connections;
using Leistd.EventBus.Distributed.Errors;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace Leistd.EventBus.EntityFrameworkCore.Stores;

internal sealed record DistributedEventDatabaseTarget(Guid? TenantId, bool AllTenants);

internal sealed class DistributedEventDatabaseScope<TDbContext>(
    ICurrentTenant tenant,
    ITenantDatabaseEnumerator databases,
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts) where TDbContext : DbContext
{
    internal static readonly string ConnectionName = typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name ?? ConnectionStringNames.Default;

    public async Task<DistributedEventDatabaseTarget> ResolveAsync(string? database, bool hostOnly, CancellationToken ct)
    {
        var caller = tenant.Id;
        if (caller is not null)
        {
            if (database is not null || hostOnly) throw new BusinessException(DistributedEventErrorCodes.HostOnly, "This operation is only available to the host.");
            return new(caller, false);
        }
        if (database is null or "host") return new(null, true);
        var list = await ListAsync(ct);
        if (!Guid.TryParse(database, out var id) || !list.Databases.Any(x => x.TenantId == id))
            throw new BusinessException(DistributedEventErrorCodes.DatabaseNotFound, "The selected database does not exist.");
        return new(hostOnly ? null : id, true);
    }

    public Task<TenantDatabaseSet> ListAsync(CancellationToken ct)
    {
        if (tenant.Id is not null) throw new BusinessException(DistributedEventErrorCodes.HostOnly, "The database directory is only available to the host.");
        return databases.GetDatabasesAsync(ConnectionName, activeOnly: false, ct);
    }

    public async Task<TResult> RunAsync<TResult>(DistributedEventDatabaseTarget target, Func<TDbContext, CancellationToken, Task<TResult>> action, CancellationToken ct)
    {
        using var change = tenant.Change(target.TenantId);
        using var unit = units.Begin(requiresNew: true);
        var db = await contexts.GetDbContextAsync(ct);
        var result = await action(db, ct);
        await unit.CompleteAsync(ct);
        return result;
    }
}
