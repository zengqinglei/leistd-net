using System.Text.Json;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Errors;
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
// 不在一个工作单元内换租户或物理库；每个成功批次的留痕与该批更新同事务登记。
internal sealed class DistributedEventRecovery<TDbContext>(
    DistributedEventDatabaseScope<TDbContext> scope,
    DistributedEventQueue<TDbContext> queue,
    IUnitOfWorkManager units,
    IServiceProvider services,
    TimeProvider clock) where TDbContext : DbContext
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
                    { var isId = Guid.TryParse(key, out var id); query = query.Where(x => (isId && x.EventId == id) || x.OrderingKey == key); }
                }
                return await query.OrderBy(x => x.Id).Take(500).Select(x => new Target(x.Id, x.TenantId)).ToListAsync(token);
            }, ct);
            Missing(requested, batch, skipped);
            foreach (var group in batch.GroupBy(x => x.TenantId))
            {
                var ids = group.Select(x => x.Id).ToArray();
                affected += await scope.RunAsync(new(group.Key, false), async (db, token) =>
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
                    Audit(DistributedEventManagementAction.Redelivered, changed, input.Filter, database);
                    return changed.Count;
                }, ct);
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
                    { var isId = Guid.TryParse(key, out var id); query = query.Where(x => (isId && x.EventId == id) || x.OrderingKey == key); }
                }
                return await query.OrderBy(x => x.Id).Take(500).Select(x => new Target(x.Id, x.TenantId)).ToListAsync(token);
            }, ct);
            Missing(requested, batch, skipped);
            foreach (var group in batch.GroupBy(x => x.TenantId))
            {
                var ids = group.Select(x => x.Id).ToArray();
                affected += await scope.RunAsync(new(group.Key, false), async (db, token) =>
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
                    Audit(DistributedEventManagementAction.Replayed, changed, input.Filter, database);
                    return changed.Count;
                }, ct);
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
        long affected = 0, after = 0;
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
                    { var isId = Guid.TryParse(key, out var id); query = query.Where(x => isId && x.EventId == id); }
                }
                return await query.OrderBy(x => x.Id).Take(500).ToListAsync(token);
            }, ct);
            if (requested is not null)
                foreach (var id in requested.Except(batch.Select(x => x.Id))) Skip(skipped, id, DistributedEventRecoverySkipReasons.NotFound);
            var outcomes = new Dictionary<long, string?>();
            foreach (var row in batch)
            {
                if (row.Status != DistributedEventRejectionStatus.Pending)
                { Skip(skipped, row.Id, DistributedEventRecoverySkipReasons.InvalidState); continue; }
                if (discard) { outcomes[row.Id] = null; continue; }
                var raw = new RawDistributedEvent { Body = row.RawBody, Headers = row.Headers, Coordinates = row.Coordinates, ReceivedAt = row.ReceivedAt };
                try
                {
                    var receiver = (EfCoreDistributedEventReceiver<TDbContext>)services.GetRequiredService<IDistributedEventReceiver>();
                    var message = await receiver.DecodeAsync(raw, ct);
                    var accepted = await receiver.AcceptAsync(raw, message, ct, persistRejection: false, enqueue: requested is not null);
                    outcomes[row.Id] = accepted ? null : "The message could not be accepted after decoding.";
                }
                catch (DistributedEventDecodeException exception) { outcomes[row.Id] = DistributedEventRetry.Truncate(exception.Message); }
                if (outcomes[row.Id] is not null) Skip(skipped, row.Id, DistributedEventRecoverySkipReasons.RedecodeFailed);
            }
            affected += await scope.RunAsync(target, async (db, token) =>
            {
                var ids = outcomes.Keys.ToArray();
                var rows = await db.Set<DistributedEventRejection>().Where(x => ids.Contains(x.Id)).ToListAsync(token);
                var changed = new List<long>();
                foreach (var row in rows)
                {
                    if (row.Status != DistributedEventRejectionStatus.Pending) continue;
                    row.LastModificationTime = clock.GetUtcNow().UtcDateTime;
                    if (!discard) row.RedecodeCount++;
                    if (outcomes[row.Id] is { } error) { row.Error = error; continue; }
                    row.Status = discard ? DistributedEventRejectionStatus.Discarded : DistributedEventRejectionStatus.Redecoded;
                    row.ResolvedAt = row.LastModificationTime;
                    changed.Add(row.Id);
                }
                Audit(discard ? DistributedEventManagementAction.Discarded : DistributedEventManagementAction.Redecoded, changed, input.Filter, database);
                return changed.Count;
            }, ct);
            if (batch.Count < 500 || requested is not null) break;
            after = batch[^1].Id;
        }
        return new() { AffectedCount = affected, Skipped = skipped };
    }

    private void Audit(DistributedEventManagementAction action, List<long> ids, object? filter, string? database)
    {
        if (ids.Count == 0) return;
        units.Current!.AddPendingEvents([new DistributedEventManagedEvent
        {
            Action = action, AffectedCount = ids.Count, Database = database,
            Ids = filter is null ? ids.ToArray() : null,
            FilterJson = filter is null ? null : JsonSerializer.Serialize(filter, filter.GetType(), JsonSerializerOptions.Web)
        }]);
    }
    private static void Missing(long[]? requested, List<Target> batch, List<DistributedEventRecoverySkipDto> skipped)
    {
        if (requested is not null)
            foreach (var id in requested.Except(batch.Select(x => x.Id))) Skip(skipped, id, DistributedEventRecoverySkipReasons.NotFound);
    }
    private static void Skip(List<DistributedEventRecoverySkipDto> skips, long id, string reason) => skips.Add(new() { Id = id, ReasonCode = reason });
    private sealed record Target(long Id, Guid? TenantId);
}
