using Leistd.EventBus.Distributed.Errors;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace Leistd.EventBus.EntityFrameworkCore.Stores;

internal sealed class DistributedEventDatabaseScope<TDbContext>(
    ICurrentTenant tenant,
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts) where TDbContext : DbContext
{
    public Guid? Resolve(bool hostOnly)
    {
        if (hostOnly && tenant.Id is not null)
            throw new BusinessException(DistributedEventErrorCodes.HostOnly, "This operation is only available to the host.");
        return tenant.Id;
    }

    public async Task<TResult> RunAsync<TResult>(Guid? target, Func<TDbContext, CancellationToken, Task<TResult>> action, CancellationToken ct)
    {
        using var change = tenant.Change(target);
        using var unit = units.Begin(requiresNew: true);
        var db = await contexts.GetDbContextAsync(ct);
        var result = await action(db, ct);
        await unit.CompleteAsync(ct);
        return result;
    }
}
