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
using Microsoft.Extensions.DependencyInjection;

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
        var unit = units.Current
            ?? throw new InvalidOperationException("Distributed events must be published inside an active unit of work.");
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
            Headers = headers, TraceId = DistributedEventTrace.FromHeaders(headers),
            CorrelationId = headers.GetValueOrDefault(DistributedEventHeaders.CorrelationId), CreationTime = now, LastModificationTime = now,
            NextAttemptAt = now.AddMinutes(1)
        };
        var db = await contexts.GetDbContextAsync(cancellationToken);
        var published = unit.ServiceProvider.GetRequiredService<PublishedEvents>().Records;
        var key = (record.TenantId, record.EventId);
        if (published.TryGetValue(key, out var previous))
        {
            if (previous.EventName != record.EventName || previous.Payload != record.Payload)
                throw new InvalidOperationException($"Event '{record.EventId}' named '{record.EventName}' was already published with different content in this unit of work.");
            return;
        }
        db.Set<DistributedEventOutbox>().Add(record);
        published.Add(key, (record.EventName, record.Payload));
        queue.OutboxAfterCommit(record);
    }

    // 由工作单元自己的 DI 作用域持有，只登记实际发布；不把已加载的历史行当成本单元发布。
    internal sealed class PublishedEvents
    {
        internal Dictionary<(Guid? TenantId, Guid EventId), (string EventName, string Payload)> Records { get; } = [];
    }
}
