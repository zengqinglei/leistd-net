using System.Diagnostics;
using System.Reflection;
using Leistd.AmbientContext;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.EventBus.Distributed.Errors;
using Leistd.UnitOfWork.Options;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore.Receiving;
using Leistd.EventBus.EventHandlers;
using Leistd.MultiTenancy.Context;
using Leistd.Tracing.Abstractions;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Dispatching;

internal sealed class DistributedEventInboxProcessor<TDbContext>(
    ICurrentTenant tenant,
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts,
    IDistributedEventSubscriptions subscriptions,
    IOptionsMonitor<DistributedEventOptions> options,
    TimeProvider clock,
    ILogger<DistributedEventInboxProcessor<TDbContext>> logger,
    ICorrelationIdProvider? correlation = null,
    IAmbientContext? ambient = null) where TDbContext : DbContext
{
    public async Task ProcessAsync(Guid? tenantId, long id, int? expectedAttempts, CancellationToken ct)
    {
        if (!options.CurrentValue.Inbox.ProcessingEnabled) return;
        using var identity = ambient?.Restore(ambient.Capture() with { Principal = null });
        using var change = tenant.Change(tenantId);
        var attempt = -1;
        Exception failure;
        using (var unit = units.Begin(new UnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var db = await contexts.GetDbContextAsync(ct);
            var row = await db.Set<DistributedEventInbox>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
            if (row is not { Status: DistributedEventInboxStatus.Pending }
                || expectedAttempts is { } expected && row.AttemptCount != expected) return;
            if (row.OrderingKey is not null && await db.Set<DistributedEventInbox>().AnyAsync(x =>
                x.TenantId == tenantId && x.EventName == row.EventName && x.Consumer == row.Consumer && x.OrderingKey == row.OrderingKey
                && x.Id < row.Id && x.Status == DistributedEventInboxStatus.Pending, ct)) return;
            attempt = row.AttemptCount;
            try
            {
                var subscription = subscriptions.GetAll().SingleOrDefault(x => x.EventName == row.EventName && x.Consumer == row.Consumer)
                    ?? throw new InvalidOperationException("The distributed event consumer is no longer registered.");
                row.Headers.TryGetValue(DistributedEventHeaders.TraceParent, out var parent);
                row.Headers.TryGetValue(DistributedEventHeaders.TraceState, out var state);
                ActivityContext.TryParse(parent, state, isRemote: true, out var parentContext);
                using var activity = DistributedEventDiagnostics.ActivitySource.StartActivity("event.process", ActivityKind.Consumer, parentContext);
                using var linkedCorrelation = row.Headers.TryGetValue(DistributedEventHeaders.CorrelationId, out var correlationId)
                    ? correlation?.Change(correlationId) : null;
                var instance = DistributedEventPayload.Deserialize(row.Payload, subscription.EventType);
                // 处理器在工作单元自己的作用域解析，业务写入与 Processed 标记共享同一上下文与事务。
                var handler = unit.ServiceProvider.GetRequiredService(subscription.HandlerType);
                var method = typeof(IEventHandler<>).MakeGenericType(subscription.EventType).GetMethod("HandleAsync")!;
                await (Task)method.Invoke(handler, [instance, ct])!;
                row.AttemptCount++;
                row.Status = DistributedEventInboxStatus.Processed;
                row.ProcessedAt = row.LastModificationTime = clock.GetUtcNow().UtcDateTime;
                row.NextAttemptAt = null;
                row.LastError = row.LastErrorCode = null;
                await unit.CompleteAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException exception) when (exception.Entries.Count > 0
                && exception.Entries.All(entry => entry.Entity is DistributedEventInbox))
            {
                await unit.RollbackAsync(CancellationToken.None);
                return;
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                failure = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                await unit.RollbackAsync(CancellationToken.None);
            }
        }
        using var failed = units.Begin(requiresNew: true);
        var context = await contexts.GetDbContextAsync(ct);
        var current = await context.Set<DistributedEventInbox>().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (current is not { Status: DistributedEventInboxStatus.Pending } || current.AttemptCount != attempt) return;
        current.AttemptCount++;
        current.LastErrorCode = DistributedEventInboxFailureCodes.HandlerException;
        current.LastError = DistributedEventRetry.Truncate(failure.Message);
        current.LastModificationTime = clock.GetUtcNow().UtcDateTime;
        if (current.AttemptCount >= options.CurrentValue.MaxAttempts)
        {
            current.Status = DistributedEventInboxStatus.Failed;
            current.NextAttemptAt = null;
            logger.LogError(new EventId(4103, "DistributedEventConsumeFailed"), failure,
                "Distributed event {EventId} exhausted attempts for {Consumer}.", current.EventId, current.Consumer);
        }
        else current.NextAttemptAt = current.LastModificationTime + DistributedEventRetry.Delay(current.AttemptCount, options.CurrentValue);
        try { await failed.CompleteAsync(ct); }
        catch (DbUpdateConcurrencyException) { await failed.RollbackAsync(CancellationToken.None); }
    }
}
