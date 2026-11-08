using System.Text.Json;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Dispatching;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.EventBus.EntityFrameworkCore.Receiving;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.EventBus.EntityFrameworkCore.Stores;

// 先在查看范围读本批目标，结束读取事务后按行所属租户分组写入。
// 不在一个工作单元内换租户或物理库；目标提交后才在调用方上下文独立提交审计。
internal sealed class DistributedEventRecovery<TDbContext>(
    DistributedEventDatabaseScope<TDbContext> scope,
    DistributedEventQueue<TDbContext> queue,
    IUnitOfWorkManager units,
    IServiceProvider services,
    TimeProvider clock,
    IDistributedEventTransport? transport = null) where TDbContext : DbContext
{
    public async Task<DistributedEventRecoveryOutputDto> OutboxAsync(RecoverDistributedEventOutboxInputDto input, string? database, CancellationToken ct)
    {
        var target = await scope.ResolveAsync(database, false, ct);
        var requested = input.Ids?.Distinct().ToArray();
        var skipped = new List<DistributedEventRecoverySkipDto>();
        long affected = 0, after = 0;
        while (true)
        {
            var batch = await scope.RunAsync(target, async (db, token) =>
            {
                var query = EfCoreDistributedEventManagementStore<TDbContext>.Outbox(db, target).AsNoTracking().Where(x => x.Id > after);
                if (requested is not null) query = query.Where(x => requested.Contains(x.Id));
                else
                {
                    var filter = input.Filter!;
                    query = query.Where(x => x.Status == DistributedEventOutboxStatus.DeadLettered && x.CreationTime <= filter.Before);
                    if (filter.EventName is not null) query = query.Where(x => x.EventName == filter.EventName);
                    if (filter.ErrorCode is not null) query = query.Where(x => x.LastErrorCode == filter.ErrorCode);
                    if (filter.Keyword is { } key)
                    { var isId = Guid.TryParse(key, out var id); var traceId = DistributedEventTrace.FromKeyword(key);
                        query = query.Where(x => (isId && x.EventId == id) || x.OrderingKey == key || (traceId != null && x.TraceId == traceId) || x.CorrelationId == key); }
                }
                return await query.OrderBy(x => x.Id).Take(500).Select(x => new Target(x.Id, x.TenantId)).ToListAsync(token);
            }, ct);
            Missing(requested, batch, skipped);
            foreach (var group in batch.GroupBy(x => x.TenantId))
            {
                var ids = group.Select(x => x.Id).ToArray();
                var changed = await scope.RunAsync(new(group.Key, false), async (db, token) =>
                {
                    var rows = await db.Set<DistributedEventOutbox>().IgnoreQueryFilters().Where(x => ids.Contains(x.Id) && x.TenantId == group.Key).ToListAsync(token);
                    var changed = new List<long>();
                    foreach (var id in ids)
                    {
                        var row = rows.SingleOrDefault(x => x.Id == id);
                        if (row is null) { Skip(skipped, id, DistributedEventRecoverySkipReasons.NotFound); continue; }
                        if (row.Status != DistributedEventOutboxStatus.DeadLettered) { Skip(skipped, id, DistributedEventRecoverySkipReasons.InvalidState); continue; }
                        row.Status = DistributedEventOutboxStatus.Pending;
                        row.NextAttemptAt = row.LastModificationTime = clock.GetUtcNow().UtcDateTime;
                        row.FirstFailureAt = null;
                        row.LastError = row.LastErrorCode = null;
                        row.RedeliverCount++;
                        changed.Add(id);
                        if (requested is not null) queue.OutboxAfterCommit(row);
                    }
                    return changed;
                }, ct);
                affected += changed.Count;
                await AuditAsync(DistributedEventManagementAction.Redelivered, changed, input.Filter, database, group.Key, ct);
            }
            if (batch.Count < 500 || requested is not null) break;
            after = batch[^1].Id;
        }
        return new() { AffectedCount = affected, Skipped = skipped };
    }

    public async Task<DistributedEventRecoveryOutputDto> InboxAsync(RecoverDistributedEventInboxInputDto input, string? database, CancellationToken ct)
    {
        var target = await scope.ResolveAsync(database, false, ct);
        var requested = input.Ids?.Distinct().ToArray();
        var skipped = new List<DistributedEventRecoverySkipDto>();
        long affected = 0, after = 0;
        while (true)
        {
            var batch = await scope.RunAsync(target, async (db, token) =>
            {
                var query = EfCoreDistributedEventManagementStore<TDbContext>.Inbox(db, target).AsNoTracking().Where(x => x.Id > after);
                if (requested is not null) query = query.Where(x => requested.Contains(x.Id));
                else
                {
                    var filter = input.Filter!;
                    query = query.Where(x => x.Status == filter.Status && x.ReceivedAt <= filter.Before);
                    if (filter.EventName is not null) query = query.Where(x => x.EventName == filter.EventName);
                    if (filter.Consumer is not null) query = query.Where(x => x.Consumer == filter.Consumer);
                    if (filter.ErrorCode is not null) query = query.Where(x => x.LastErrorCode == filter.ErrorCode);
                    if (filter.Keyword is { } key)
                    { var isId = Guid.TryParse(key, out var id); var traceId = DistributedEventTrace.FromKeyword(key);
                        query = query.Where(x => (isId && x.EventId == id) || x.OrderingKey == key || (traceId != null && x.TraceId == traceId) || x.CorrelationId == key); }
                }
                return await query.OrderBy(x => x.Id).Take(500).Select(x => new Target(x.Id, x.TenantId)).ToListAsync(token);
            }, ct);
            Missing(requested, batch, skipped);
            foreach (var group in batch.GroupBy(x => x.TenantId))
            {
                var ids = group.Select(x => x.Id).ToArray();
                var changed = await scope.RunAsync(new(group.Key, false), async (db, token) =>
                {
                    var rows = await db.Set<DistributedEventInbox>().IgnoreQueryFilters().Where(x => ids.Contains(x.Id) && x.TenantId == group.Key).ToListAsync(token);
                    var changed = new List<long>();
                    foreach (var id in ids)
                    {
                        var row = rows.SingleOrDefault(x => x.Id == id);
                        if (row is null) { Skip(skipped, id, DistributedEventRecoverySkipReasons.NotFound); continue; }
                        if (row.Status is not (DistributedEventInboxStatus.Failed or DistributedEventInboxStatus.Processed))
                        { Skip(skipped, id, DistributedEventRecoverySkipReasons.InvalidState); continue; }
                        row.Status = DistributedEventInboxStatus.Pending;
                        row.AttemptCount = 0;
                        row.NextAttemptAt = row.LastModificationTime = clock.GetUtcNow().UtcDateTime;
                        row.LastError = row.LastErrorCode = null;
                        row.ProcessedAt = null;
                        row.ReplayCount++;
                        changed.Add(id);
                        if (requested is not null) queue.InboxAfterCommit(row);
                    }
                    return changed;
                }, ct);
                affected += changed.Count;
                await AuditAsync(DistributedEventManagementAction.Replayed, changed, input.Filter, database, group.Key, ct);
            }
            if (batch.Count < 500 || requested is not null) break;
            after = batch[^1].Id;
        }
        return new() { AffectedCount = affected, Skipped = skipped };
    }

    public async Task<DistributedEventRecoveryOutputDto> RejectionsAsync(RecoverDistributedEventRejectionInputDto input, string? database, bool discard, CancellationToken ct)
    {
        var target = await scope.ResolveAsync(database, true, ct);
        var requested = input.Ids?.Distinct().ToArray();
        var skipped = new List<DistributedEventRecoverySkipDto>();
        long affected = 0, deferredCount = 0, after = 0;
        while (true)
        {
            var batch = await scope.RunAsync(target, async (db, token) =>
            {
                var query = db.Set<DistributedEventRejection>().AsNoTracking().Where(x => x.Id > after);
                if (requested is not null) query = query.Where(x => requested.Contains(x.Id));
                else
                {
                    var filter = input.Filter!;
                    query = query.Where(x => x.Status == DistributedEventRejectionStatus.Pending && x.ReceivedAt <= filter.Before);
                    if (filter.EventName is not null) query = query.Where(x => x.EventName == filter.EventName);
                    if (filter.Reason is not null) query = query.Where(x => x.Reason == filter.Reason);
                    if (filter.Keyword is { } key)
                    { var isId = Guid.TryParse(key, out var id); var traceId = DistributedEventTrace.FromKeyword(key);
                        query = query.Where(x => (isId && x.EventId == id) || (traceId != null && x.TraceId == traceId) || x.CorrelationId == key); }
                }
                return await query.OrderBy(x => x.Id).Take(500).ToListAsync(token);
            }, ct);
            if (requested is not null)
                foreach (var id in requested.Except(batch.Select(x => x.Id))) Skip(skipped, id, DistributedEventRecoverySkipReasons.NotFound);
            var eligible = batch.Where(row => row.Status == DistributedEventRejectionStatus.Pending).ToArray();
            foreach (var row in batch.Except(eligible)) Skip(skipped, row.Id, DistributedEventRecoverySkipReasons.InvalidState);
            foreach (var group in eligible.GroupBy(row => !discard && (transport is null || row.Transport != transport.Name)))
            {
                var deferred = group.Key;
                var outcomes = new Dictionary<long, string?>();
                foreach (var row in group)
                {
                    outcomes[row.Id] = discard || deferred ? null : await DecodeAsync(row, requested is not null, ct);
                    if (outcomes[row.Id] is not null) Skip(skipped, row.Id, DistributedEventRecoverySkipReasons.RedecodeFailed);
                }
                var changed = await scope.RunAsync(target, async (db, token) =>
                {
                    var ids = outcomes.Keys.ToArray();
                    var rows = await db.Set<DistributedEventRejection>().Where(x => ids.Contains(x.Id)).ToListAsync(token);
                    var changed = new List<long>();
                    foreach (var row in rows)
                    {
                        if (row.Status != DistributedEventRejectionStatus.Pending) continue;
                        row.LastModificationTime = clock.GetUtcNow().UtcDateTime;
                        if (!discard && !deferred) row.RedecodeCount++;
                        if (outcomes[row.Id] is { } error) { row.Error = error; continue; }
                        row.Status = discard ? DistributedEventRejectionStatus.Discarded
                            : deferred ? DistributedEventRejectionStatus.RedecodePending : DistributedEventRejectionStatus.Redecoded;
                        row.ResolvedAt = deferred ? null : row.LastModificationTime;
                        changed.Add(row.Id);
                    }
                    return changed;
                }, ct);
                affected += changed.Count;
                if (deferred) deferredCount += changed.Count;
                await AuditAsync(discard ? DistributedEventManagementAction.Discarded
                    : deferred ? DistributedEventManagementAction.RedecodeRequested : DistributedEventManagementAction.Redecoded,
                    changed, input.Filter, database, null, ct);
            }
            if (batch.Count < 500 || requested is not null) break;
            after = batch[^1].Id;
        }
        return new() { AffectedCount = affected, DeferredCount = deferredCount, Skipped = skipped };
    }

    // 只领取匹配传输的宿主拒收；接收提交后再结束拒收，进程中断时由 Inbox 唯一约束防重。
    internal async Task RedecodePendingAsync(CancellationToken ct)
    {
        if (transport is null) return;
        long after = 0;
        var target = new DistributedEventDatabaseTarget(null, true);
        while (true)
        {
            var batch = await scope.RunAsync(target, (db, token) => db.Set<DistributedEventRejection>().AsNoTracking()
                .Where(x => x.Id > after && x.Status == DistributedEventRejectionStatus.RedecodePending && x.Transport == transport.Name)
                .OrderBy(x => x.Id).Take(100).ToListAsync(token), ct);
            foreach (var row in batch)
            {
                var error = await DecodeAsync(row, true, ct);
                await scope.RunAsync(target, async (db, token) =>
                {
                    var current = await db.Set<DistributedEventRejection>().SingleAsync(x => x.Id == row.Id, token);
                    if (current.Status != DistributedEventRejectionStatus.RedecodePending) return false;
                    current.RedecodeCount++;
                    current.LastModificationTime = clock.GetUtcNow().UtcDateTime;
                    current.Status = error is null ? DistributedEventRejectionStatus.Redecoded : DistributedEventRejectionStatus.Pending;
                    current.ResolvedAt = error is null ? current.LastModificationTime : null;
                    if (error is not null) current.Error = error;
                    return true;
                }, ct);
            }
            if (batch.Count < 100) break;
            after = batch[^1].Id;
        }
    }

    private async Task<string?> DecodeAsync(DistributedEventRejection row, bool enqueue, CancellationToken ct)
    {
        var raw = new RawDistributedEvent { Body = row.RawBody, Headers = row.Headers, Coordinates = row.Coordinates, ReceivedAt = row.ReceivedAt };
        try
        {
            var receiver = (EfCoreDistributedEventReceiver<TDbContext>)services.GetRequiredService<IDistributedEventReceiver>();
            var message = await receiver.DecodeAsync(raw, ct);
            var accepted = await receiver.AcceptAsync(raw, message, ct, persistRejection: false, enqueue: enqueue);
            return accepted ? null : "The message could not be accepted after decoding.";
        }
        catch (DistributedEventDecodeException exception) { return DistributedEventRetry.Truncate(exception.Message); }
    }

    private async Task AuditAsync(DistributedEventManagementAction action, List<long> ids, object? filter, string? database, Guid? targetTenantId, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        // 目标批次已经提交，不能再受调用方外层事务的提交或回滚控制。
        using var audit = units.Begin(requiresNew: true);
        audit.AddPendingEvents([new DistributedEventManagedEvent
        {
            Action = action, AffectedCount = ids.Count, Database = database, TargetTenantId = targetTenantId,
            Ids = filter is null ? ids.ToArray() : null,
            FilterJson = filter is null ? null : JsonSerializer.Serialize(filter, filter.GetType(), JsonSerializerOptions.Web)
        }]);
        await audit.CompleteAsync(ct);
    }
    private static void Missing(long[]? requested, List<Target> batch, List<DistributedEventRecoverySkipDto> skipped)
    {
        if (requested is not null)
            foreach (var id in requested.Except(batch.Select(x => x.Id))) Skip(skipped, id, DistributedEventRecoverySkipReasons.NotFound);
    }
    private static void Skip(List<DistributedEventRecoverySkipDto> skips, long id, string reason) => skips.Add(new() { Id = id, ReasonCode = reason });
    private sealed record Target(long Id, Guid? TenantId);
}
