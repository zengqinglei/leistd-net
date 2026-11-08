using System.Diagnostics;
using System.Text.Json;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.Events;
using Leistd.MultiTenancy.Context;
using Leistd.Tracing.Abstractions;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace Leistd.EventBus.EntityFrameworkCore.Publishing;

internal sealed class EfCoreDistributedEventBus<TDbContext>(
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts,
    ICurrentTenant tenant,
    DistributedEventQueue<TDbContext> queue,
    TimeProvider clock,
    ICorrelationIdProvider? correlation = null) : IDistributedEventBus
    where TDbContext : DbContext
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        => PublishAsync((IEvent)@event, cancellationToken);

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event is not IDistributedEvent)
            throw new ArgumentException("Distributed publishing requires IDistributedEvent.", nameof(@event));
        if (units.Current is null)
            throw new InvalidOperationException("Distributed events must be published inside an active unit of work.");
        if (@event.EventId == Guid.Empty || @event.OccurredOn.Kind != DateTimeKind.Utc)
            throw new ArgumentException("EventId must be nonempty and OccurredOn must be UTC.", nameof(@event));

        using var activity = DistributedEventDiagnostics.ActivitySource.StartActivity("event.publish", ActivityKind.Producer);
        var headers = new Dictionary<string, string>();
        if (Activity.Current?.Id is { } trace) headers[DistributedEventHeaders.TraceParent] = trace;
        if (Activity.Current?.TraceStateString is { } state) headers[DistributedEventHeaders.TraceState] = state;
        if (correlation?.Get() is { } id) headers[DistributedEventHeaders.CorrelationId] = id;
        var now = clock.GetUtcNow().UtcDateTime;
        var record = new DistributedEventOutbox
        {
            EventId = @event.EventId, EventName = DistributedEventNames.Get(@event.GetType()),
            TenantId = tenant.Id, OrderingKey = (@event as IOrderedDistributedEvent)?.OrderingKey,
            OccurredAt = @event.OccurredOn, Payload = JsonSerializer.Serialize(@event, @event.GetType(), JsonSerializerOptions.Web),
            Headers = headers, CreationTime = now, LastModificationTime = now,
            NextAttemptAt = now.AddMinutes(1)
        };
        var db = await contexts.GetDbContextAsync(cancellationToken);
        db.Set<DistributedEventOutbox>().Add(record);
        queue.OutboxAfterCommit(record);
    }
}
