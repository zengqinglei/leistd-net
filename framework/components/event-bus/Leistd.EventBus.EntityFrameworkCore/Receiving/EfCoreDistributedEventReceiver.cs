using System.Reflection;
using System.Text.Json;
using Leistd.Data.Connections;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Stores;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.EventBus.EntityFrameworkCore.Receiving;

internal sealed class EfCoreDistributedEventReceiver<TDbContext>(
    IDistributedEventTransport transport,
    IDistributedEventSubscriptions subscriptions,
    ICurrentTenant tenant,
    IUnitOfWorkManager units,
    IDbContextProvider<TDbContext> contexts,
    DistributedEventQueue<TDbContext> queue,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<EfCoreDistributedEventReceiver<TDbContext>> logger) : IDistributedEventReceiver
    where TDbContext : DbContext
{
    private static readonly string ConnectionName = typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name
        ?? ConnectionStringNames.Default;

    public async Task ReceiveAsync(RawDistributedEvent raw, CancellationToken cancellationToken)
    {
        IncomingDistributedEvent message;
        try
        {
            message = await DecodeAsync(raw, cancellationToken);
        }
        catch (DistributedEventDecodeException exception)
        {
            await RejectAsync(raw, exception.ReasonCode, exception.Message, cancellationToken);
            return;
        }
        await AcceptAsync(raw, message, cancellationToken);
    }

    internal async ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken ct)
    {
        var message = await transport.DecodeAsync(raw, ct);
        if (message.EventId == Guid.Empty || message.OccurredAt.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(message.EventName))
            throw new DistributedEventDecodeException(DistributedEventRejectionReasons.InvalidEnvelope, "The decoded event metadata is invalid.");
        return message;
    }

    // 管理重新解码也走同一接收边界，但须知道是否真正接收，避免新增另一条拒收记录。
    internal async Task<bool> AcceptAsync(RawDistributedEvent raw, IncomingDistributedEvent message, CancellationToken ct, bool persistRejection = true, bool enqueue = true)
    {
        if (message.TenantId is { } tenantId && !await TenantExistsAsync(tenantId, ct))
        {
            if (persistRejection) await RejectMessageAsync(raw, message, DistributedEventRejectionReasons.UnknownTenant, "The message tenant does not exist.", ct);
            return false;
        }
        var consumers = subscriptions.GetAll().Where(x => x.EventName == message.EventName).ToArray();
        if (consumers.Length == 0)
        {
            logger.LogDebug("No consumer is registered for distributed event {EventName}.", message.EventName);
            return true;
        }
        string payload;
        try
        {
            payload = DistributedEventPayload.Prepare(message);
            foreach (var consumer in consumers)
            {
                var instance = DistributedEventPayload.Deserialize(payload, consumer.EventType);
                if (instance.EventId != message.EventId || instance.OccurredOn != message.OccurredAt)
                    throw new JsonException("The event contract cannot restore envelope metadata.");
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            if (persistRejection) await RejectMessageAsync(raw, message, DistributedEventRejectionReasons.InvalidPayload, exception.Message, ct);
            return false;
        }
        using var change = tenant.Change(message.TenantId);
        try { await PersistAsync(message, raw.ReceivedAt, payload, consumers, false, enqueue, ct); }
        catch (DbUpdateException)
        {
            // 并发首次接收由唯一约束裁决；只有重读确认所有消费者均已存在才当作重复，其他写库故障仍向上传播。
            if (!await PersistAsync(message, raw.ReceivedAt, payload, consumers, true, enqueue, ct)) throw;
        }
        return true;
    }

    private async Task<bool> TenantExistsAsync(Guid id, CancellationToken ct)
    {
        // 目录可能使用另一个上下文，必须在业务工作单元之外读取。
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<ITenantStore>() is { } store)
            return await store.FindAsync(id, ct) is not null;
        var connections = scope.ServiceProvider.GetService<ITenantConnectionConfigurationStore>()
            ?? throw new InvalidOperationException("Receiving tenant events requires ITenantStore or ITenantConnectionConfigurationStore.");
        return await connections.FindAsync(id, ConnectionName, ct) is not null;
    }

    private async Task<bool> PersistAsync(IncomingDistributedEvent message, DateTime receivedAt, string payload,
        DistributedEventSubscription[] consumers, bool duplicatesOnly, bool enqueue, CancellationToken ct)
    {
        using var unit = units.Begin(requiresNew: true);
        var db = await contexts.GetDbContextAsync(ct);
        var names = consumers.Select(x => x.Consumer).ToArray();
        var existing = await db.Set<DistributedEventInbox>()
            .Where(x => x.TenantId == message.TenantId && x.EventId == message.EventId && names.Contains(x.Consumer)).ToListAsync(ct);
        if (duplicatesOnly && existing.Count != consumers.Length) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var consumer in consumers)
        {
            var row = existing.SingleOrDefault(x => x.Consumer == consumer.Consumer);
            if (row is not null)
            {
                row.DuplicateCount++;
                row.LastModificationTime = now;
                continue;
            }
            row = new DistributedEventInbox
            {
                EventId = message.EventId, EventName = message.EventName, TenantId = message.TenantId,
                Consumer = consumer.Consumer, OrderingKey = message.OrderingKey, OccurredAt = message.OccurredAt,
                Payload = payload, Headers = new(message.Headers), Coordinates = new(message.Coordinates),
                TraceId = DistributedEventTrace.FromHeaders(message.Headers),
                CorrelationId = message.Headers.GetValueOrDefault(DistributedEventHeaders.CorrelationId),
                ReceivedAt = receivedAt, LastModificationTime = now, NextAttemptAt = now.AddMinutes(1)
            };
            db.Set<DistributedEventInbox>().Add(row);
            if (enqueue) queue.InboxAfterCommit(row);
        }
        await unit.CompleteAsync(ct);
        return true;
    }

    public Task RejectAsync(RawDistributedEvent raw, string reasonCode, string error, CancellationToken cancellationToken)
        => RejectMessageAsync(raw, null, reasonCode, error, cancellationToken);

    private async Task RejectMessageAsync(RawDistributedEvent raw, IncomingDistributedEvent? message, string reason, string error, CancellationToken ct)
    {
        var coordinates = string.Join(";", raw.Coordinates.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
        if (coordinates.Length > 256) coordinates = coordinates[..256];
        try
        {
            using var change = tenant.Change(null);
            using var unit = units.Begin(requiresNew: true);
            var db = await contexts.GetDbContextAsync(ct);
            db.Set<DistributedEventRejection>().Add(new()
            {
                Transport = transport.Name, Reason = reason, Error = error.Length > 512 ? error[..512] : error,
                EventName = message?.EventName, EventId = message?.EventId, RawBody = raw.Body.ToArray(),
                Headers = new(raw.Headers), Coordinates = new(raw.Coordinates), ReceivedAt = raw.ReceivedAt,
                TraceId = DistributedEventTrace.FromHeaders(message?.Headers ?? raw.Headers),
                CorrelationId = (message?.Headers ?? raw.Headers).GetValueOrDefault(DistributedEventHeaders.CorrelationId),
                LastModificationTime = clock.GetUtcNow().UtcDateTime
            });
            await unit.CompleteAsync(ct);
            logger.LogWarning(new EventId(4104, "DistributedEventRejected"), "Distributed event {EventId} ({EventName}) rejected by {Transport}: {Reason}. Coordinates: {Coordinates}.",
                message?.EventId, message?.EventName, transport.Name, reason, coordinates);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogError(new EventId(4105, "DistributedEventRejectPersistFailed"), exception, "Rejected distributed event {EventId} ({EventName}) from {Transport} could not be persisted: {Reason}. Coordinates: {Coordinates}.",
                message?.EventId, message?.EventName, transport.Name, reason, coordinates);
            throw;
        }
    }
}
