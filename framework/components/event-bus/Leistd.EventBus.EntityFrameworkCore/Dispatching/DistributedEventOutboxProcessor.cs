using System.Diagnostics;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.Tracing.Abstractions;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore.Receiving;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Dispatching;

internal sealed class DistributedEventOutboxProcessor<TDbContext>(
    ICurrentTenant tenant,
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts,
    IOptionsMonitor<DistributedEventOptions> options,
    TimeProvider clock,
    ILogger<DistributedEventOutboxProcessor<TDbContext>> logger,
    IServiceProvider services,
    ICorrelationIdProvider? correlation = null,
    IDistributedEventTransport? transport = null) where TDbContext : DbContext
{
    public async Task ProcessAsync(Guid? tenantId, long id, int? expectedAttempts, CancellationToken ct)
    {
        if (!options.CurrentValue.Outbox.DispatchEnabled) return;
        using var change = tenant.Change(tenantId);
        DistributedEventOutbox row;
        using (var read = units.Begin(requiresNew: true))
        {
            var db = await contexts.GetDbContextAsync(ct);
            var candidate = await db.Set<DistributedEventOutbox>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
            if (candidate is not { Status: DistributedEventOutboxStatus.Pending }
                || expectedAttempts is { } expected && candidate.AttemptCount != expected) return;
            row = candidate;
            if (row.OrderingKey is not null && await db.Set<DistributedEventOutbox>().AnyAsync(x =>
                x.TenantId == tenantId && x.EventName == row.EventName && x.OrderingKey == row.OrderingKey
                && x.Id < row.Id && x.Status == DistributedEventOutboxStatus.Pending, ct)) return;
            await read.CompleteAsync(ct);
        }
        row.Headers.TryGetValue(DistributedEventHeaders.TraceParent, out var parent);
        row.Headers.TryGetValue(DistributedEventHeaders.TraceState, out var state);
        ActivityContext.TryParse(parent, state, isRemote: true, out var parentContext);
        var local = DistributedEventInternalDelivery.IsInternal(row.Headers);
        DistributedEventSendResult result;
        try
        {
            using var activity = DistributedEventDiagnostics.ActivitySource.StartActivity("event.send", ActivityKind.Producer, parentContext);
            using var linkedCorrelation = row.Headers.TryGetValue(DistributedEventHeaders.CorrelationId, out var correlationId)
                ? correlation?.Change(correlationId) : null;
            result = local ? await DeliverInternalAsync(row, ct)
                : await (transport ?? throw new InvalidOperationException("Sending requires an IDistributedEventTransport registration.")).SendAsync(new()
                {
                    EventId = row.EventId, EventName = row.EventName, TenantId = row.TenantId, OrderingKey = row.OrderingKey,
                    OccurredAt = row.OccurredAt, Payload = row.Payload, Headers = row.Headers
                }, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            result = new() { Status = DistributedEventSendStatus.TransientFailure, ErrorCode = DistributedEventOutboxFailureCodes.TransportException, Error = exception.GetType().FullName };
            if (local) logger.LogWarning(exception, "Distributed event {EventId} local delivery failed before confirmation.", row.EventId);
            else logger.LogWarning(exception, "Distributed event {EventId} transport failed before confirmation.", row.EventId);
        }
        using var write = units.Begin(requiresNew: true);
        var context = await contexts.GetDbContextAsync(ct);
        var current = await context.Set<DistributedEventOutbox>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (current is not { Status: DistributedEventOutboxStatus.Pending } || current.AttemptCount != row.AttemptCount) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var config = options.CurrentValue;
        current.AttemptCount++;
        current.LastModificationTime = now;
        current.LastErrorCode = result.ErrorCode;
        current.LastError = result.Error is null ? null : DistributedEventRetry.Truncate(result.Error);
        if (result.Status == DistributedEventSendStatus.Succeeded)
        {
            current.Status = DistributedEventOutboxStatus.Sent;
            current.SentAt = now;
            current.NextAttemptAt = null;
            current.LastError = null;
            current.LastErrorCode = null;
        }
        else
        {
            current.FirstFailureAt ??= now;
            if (result.Status == DistributedEventSendStatus.PermanentFailure || now - current.FirstFailureAt.Value >= config.DeadLetterAfter)
            {
                current.Status = DistributedEventOutboxStatus.DeadLettered;
                current.NextAttemptAt = null;
                if (result.Status != DistributedEventSendStatus.PermanentFailure)
                    current.LastErrorCode = DistributedEventOutboxFailureCodes.RetryWindowExceeded;
                logger.LogError(new EventId(4101, "DistributedEventDeadLettered"),
                    "Distributed event {EventId} ({EventName}) became a dead letter: {ErrorCode}.", row.EventId, row.EventName, current.LastErrorCode);
            }
            else current.NextAttemptAt = now + DistributedEventRetry.Delay(current.AttemptCount, config);
        }
        await write.CompleteAsync(ct);
    }

    // 本地投递按派发实例本机登记的内部消费者写收件箱；找不到任何消费者不能沿用接收侧"无消费者即成功"，否则静默丢失。
    private async Task<DistributedEventSendResult> DeliverInternalAsync(DistributedEventOutbox row, CancellationToken ct)
    {
        var receiver = (EfCoreDistributedEventReceiver<TDbContext>)services.GetRequiredService<IDistributedEventReceiver>();
        var (accepted, error) = await receiver.DeliverInternalAsync(new()
        {
            EventId = row.EventId, EventName = row.EventName, TenantId = row.TenantId, OrderingKey = row.OrderingKey,
            OccurredAt = row.OccurredAt, Payload = row.Payload, Headers = row.Headers,
            Coordinates = new Dictionary<string, string> { ["transport"] = DistributedEventInternalDelivery.Transport }
        }, clock.GetUtcNow().UtcDateTime, ct);
        return accepted switch
        {
            DistributedEventAcceptance.Stored => new() { Status = DistributedEventSendStatus.Succeeded },
            DistributedEventAcceptance.NoConsumer => Permanent(DistributedEventOutboxFailureCodes.InternalNoConsumer, "No internal consumer is registered for this event."),
            DistributedEventAcceptance.InvalidPayload => Permanent(DistributedEventOutboxFailureCodes.InternalInvalidPayload, error!),
            _ => Permanent(DistributedEventOutboxFailureCodes.InternalUnknownTenant, "The event tenant does not exist.")
        };

        static DistributedEventSendResult Permanent(string code, string message)
            => new() { Status = DistributedEventSendStatus.PermanentFailure, ErrorCode = code, Error = message };
    }
}
