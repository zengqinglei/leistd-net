using Leistd.BackgroundJobs.Recurring;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Receiving;

internal sealed class DistributedEventRedecodeJob<TDbContext>(
    DistributedEventRecovery<TDbContext> recovery,
    IOptionsMonitor<DistributedEventOptions> options) : IRecurringJob where TDbContext : DbContext
{
    internal const string Name = "event-bus.redecode";

    public Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
        => options.CurrentValue.Inbox.ReceiveEnabled ? recovery.RedecodePendingAsync(cancellationToken) : Task.CompletedTask;
}
