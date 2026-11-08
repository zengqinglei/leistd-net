using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed.Subscriptions;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Stores;

internal sealed class DistributedEventCatalog<TDbContext>(
    DistributedEventDatabaseScope<TDbContext> scope,
    IDistributedEventSubscriptions subscriptions,
    IOptionsMonitor<DistributedEventOptions> options,
    ICurrentTenant tenant,
    TimeProvider clock,
    IDistributedEventTransport? transport = null) where TDbContext : DbContext
{
    public async Task<DistributedEventStatusOutputDto> StatusAsync(string? database, CancellationToken ct)
    {
        var isHost = tenant.Id is null;
        var target = await scope.ResolveAsync(database, false, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var yesterday = now.AddHours(-24);
        var result = await scope.RunAsync(target, async (db, token) =>
        {
            var outgoing = EfCoreDistributedEventManagementStore<TDbContext>.Outbox(db, target);
            var incoming = EfCoreDistributedEventManagementStore<TDbContext>.Inbox(db, target);
            var oldest = await outgoing.Where(x => x.Status == DistributedEventOutboxStatus.Pending).Select(x => (DateTime?)x.CreationTime).MinAsync(token);
            return new DistributedEventStatusOutputDto
            {
                PendingOutbox = await outgoing.LongCountAsync(x => x.Status == DistributedEventOutboxStatus.Pending, token),
                OldestPendingOutboxAgeSeconds = oldest is null ? null : Math.Max(0, (now - oldest.Value).TotalSeconds),
                DeadLettered = await outgoing.LongCountAsync(x => x.Status == DistributedEventOutboxStatus.DeadLettered, token),
                PendingInbox = await incoming.LongCountAsync(x => x.Status == DistributedEventInboxStatus.Pending, token),
                FailedInbox = await incoming.LongCountAsync(x => x.Status == DistributedEventInboxStatus.Failed, token),
                SentLast24Hours = await outgoing.LongCountAsync(x => x.SentAt >= yesterday, token),
                ProcessedLast24Hours = await incoming.LongCountAsync(x => x.ProcessedAt >= yesterday, token),
                TransportName = transport?.Name, TransportStatus = "unknown", TestEventEnabled = options.CurrentValue.TestEvent.Enabled
            };
        }, ct);
        if (isHost)
            result = result with { PendingRejections = await scope.RunAsync(new(null, true),
                (db, token) => db.Set<DistributedEventRejection>().LongCountAsync(x => x.Status == DistributedEventRejectionStatus.Pending || x.Status == DistributedEventRejectionStatus.RedecodePending, token), ct) };
        var freshAfter = now - options.CurrentValue.TransportStatus.StaleAfter;
        var states = await scope.RunAsync(new(null, true),
            (db, token) => db.Set<DistributedEventTransportState>().AsNoTracking()
                .Where(x => x.ReportedAt >= freshAfter).ToListAsync(token), ct);
        if (states.Count > 0)
        {
            var worst = states.OrderBy(x => x.Status).ThenByDescending(x => x.StatusChangedAt)
                .ThenByDescending(x => x.ReportedAt).ThenBy(x => x.Instance, StringComparer.Ordinal).First();
            result = result with
            {
                TransportName = transport?.Name ?? worst.TransportName,
                TransportStatus = worst.Status.ToString().ToLowerInvariant(),
                TransportStatusChangedAt = worst.StatusChangedAt,
                TransportReportedAt = states.Max(x => x.ReportedAt)
            };
        }
        return result;
    }

    public async Task<IReadOnlyList<DistributedEventCatalogOutputDto>> GetAsync(string? database, CancellationToken ct)
    {
        var target = await scope.ResolveAsync(database, false, ct);
        return await scope.RunAsync<IReadOnlyList<DistributedEventCatalogOutputDto>>(target, async (db, token) =>
        {
            var outgoing = EfCoreDistributedEventManagementStore<TDbContext>.Outbox(db, target);
            var incoming = EfCoreDistributedEventManagementStore<TDbContext>.Inbox(db, target);
            var sent = await outgoing.GroupBy(x => x.EventName).Select(g => new
            {
                EventName = g.Key, Last = g.Max(x => x.SentAt),
                Pending = g.LongCount(x => x.Status == DistributedEventOutboxStatus.Pending),
                Failed = g.LongCount(x => x.Status == DistributedEventOutboxStatus.DeadLettered)
            }).ToListAsync(token);
            var received = await incoming.GroupBy(x => x.EventName).Select(g => new
            {
                EventName = g.Key, Last = g.Max(x => x.ReceivedAt), Processed = g.Max(x => x.ProcessedAt),
                Pending = g.LongCount(x => x.Status == DistributedEventInboxStatus.Pending),
                Failed = g.LongCount(x => x.Status == DistributedEventInboxStatus.Failed)
            }).ToListAsync(token);
            // 每类只取最后一个错误；不加载记录载荷或把历史记录搬到内存聚合。
            var sendErrors = await outgoing.Where(x => x.LastErrorCode != null).GroupBy(x => x.EventName)
                .Select(g => g.OrderByDescending(x => x.LastModificationTime).ThenByDescending(x => x.Id)
                    .Select(x => new { x.EventName, x.LastErrorCode, x.LastModificationTime }).First()).ToListAsync(token);
            var receiveErrors = await incoming.Where(x => x.LastErrorCode != null).GroupBy(x => x.EventName)
                .Select(g => g.OrderByDescending(x => x.LastModificationTime).ThenByDescending(x => x.Id)
                    .Select(x => new { x.EventName, x.LastErrorCode, x.LastModificationTime }).First()).ToListAsync(token);
            var registered = subscriptions.GetAll();
            var names = registered.Select(x => x.EventName).Concat(sent.Select(x => x.EventName)).Concat(received.Select(x => x.EventName)).Distinct().Order().ToArray();
            return names.Select(name =>
            {
                var send = sent.SingleOrDefault(x => x.EventName == name);
                var receive = received.SingleOrDefault(x => x.EventName == name);
                var handlers = registered.Where(x => x.EventName == name).ToArray();
                var error = sendErrors.Concat(receiveErrors).Where(x => x.EventName == name).OrderByDescending(x => x.LastModificationTime).FirstOrDefault();
                return new DistributedEventCatalogOutputDto
                {
                    EventName = name, Outgoing = send is not null, Incoming = handlers.Length > 0 || receive is not null,
                    Consumers = handlers.Select(x => x.Consumer).Distinct().Order().ToArray(),
                    LastSentAt = send?.Last, LastReceivedAt = receive?.Last, LastProcessedAt = receive?.Processed,
                    PendingOutbox = send?.Pending ?? 0, PendingInbox = receive?.Pending ?? 0,
                    DeadLettered = send?.Failed ?? 0, FailedInbox = receive?.Failed ?? 0, LastErrorCode = error?.LastErrorCode
                };
            }).ToArray();
        }, ct);
    }
}
