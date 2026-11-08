using Leistd.Data.Paging;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Diagnostics;
using Leistd.EventBus.Distributed.Errors;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Stores;

internal sealed class EfCoreDistributedEventManagementStore<TDbContext>(
    DistributedEventDatabaseScope<TDbContext> scope,
    DistributedEventRecovery<TDbContext> recovery,
    DistributedEventCatalog<TDbContext> catalog,
    ICurrentTenant tenant,
    IUnitOfWorkManager units,
    IDistributedEventBus publisher,
    IOptionsMonitor<DistributedEventOptions> options) : IDistributedEventManagementStore where TDbContext : DbContext
{
    public Task<DistributedEventStatusOutputDto> GetStatusAsync(string? database = null, CancellationToken cancellationToken = default)
        => catalog.StatusAsync(database, cancellationToken);
    public Task<IReadOnlyList<DistributedEventCatalogOutputDto>> GetCatalogAsync(string? database = null, CancellationToken cancellationToken = default)
        => catalog.GetAsync(database, cancellationToken);

    public async Task<IReadOnlyList<DistributedEventDatabaseOutputDto>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var list = await scope.ListAsync(cancellationToken);
        if (list.FailedTenants.Count > 0) throw new InvalidOperationException("Some tenant databases could not be resolved.");
        return list.Databases.Select(x => new DistributedEventDatabaseOutputDto
        { Key = x.TenantId?.ToString() ?? "host", IsHost = x.TenantId is null, RepresentativeTenantId = x.TenantId, TenantIds = x.TenantIds }).ToArray();
    }

    public async Task<PagedResult<DistributedEventOutboxOutputDto>> GetOutboxListAsync(GetDistributedEventOutboxInputDto input, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(input.Database, false, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) =>
        {
            var query = Outbox(db, target);
            if (input.EventName is not null) query = query.Where(x => x.EventName == input.EventName);
            if (input.ErrorCode is not null) query = query.Where(x => x.LastErrorCode == input.ErrorCode);
            if (input.Status is { } status) query = query.Where(x => x.Status == status);
            if (input.Before is { } before) query = query.Where(x => x.CreationTime <= before);
            if (input.Keyword is { } key)
            {
                var isId = Guid.TryParse(key, out var id);
                var traceId = DistributedEventTrace.FromKeyword(key);
                query = query.Where(x => (isId && x.EventId == id) || x.OrderingKey == key || (traceId != null && x.TraceId == traceId) || x.CorrelationId == key);
            }
            return new PagedResult<DistributedEventOutboxOutputDto>(await query.LongCountAsync(ct),
                await query.OrderByDescending(x => x.Id).Skip(input.Offset).Take(input.Limit).Select(DistributedEventProjections.Outbox).ToListAsync(ct));
        }, cancellationToken);
    }

    public async Task<PagedResult<DistributedEventInboxOutputDto>> GetInboxListAsync(GetDistributedEventInboxInputDto input, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(input.Database, false, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) =>
        {
            var query = Inbox(db, target);
            if (input.EventName is not null) query = query.Where(x => x.EventName == input.EventName);
            if (input.ErrorCode is not null) query = query.Where(x => x.LastErrorCode == input.ErrorCode);
            if (input.Consumer is not null) query = query.Where(x => x.Consumer == input.Consumer);
            if (input.Status is { } status) query = query.Where(x => x.Status == status);
            if (input.Before is { } before) query = query.Where(x => x.ReceivedAt <= before);
            if (input.Keyword is { } key)
            {
                var isId = Guid.TryParse(key, out var id);
                var traceId = DistributedEventTrace.FromKeyword(key);
                query = query.Where(x => (isId && x.EventId == id) || x.OrderingKey == key || (traceId != null && x.TraceId == traceId) || x.CorrelationId == key);
            }
            return new PagedResult<DistributedEventInboxOutputDto>(await query.LongCountAsync(ct),
                await query.OrderByDescending(x => x.Id).Skip(input.Offset).Take(input.Limit).Select(DistributedEventProjections.Inbox).ToListAsync(ct));
        }, cancellationToken);
    }

    public async Task<PagedResult<DistributedEventRejectionOutputDto>> GetRejectionsAsync(GetDistributedEventRejectionsInputDto input, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(input.Database, true, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) =>
        {
            var query = db.Set<DistributedEventRejection>().AsNoTracking();
            if (input.EventName is not null) query = query.Where(x => x.EventName == input.EventName);
            if (input.Reason is not null) query = query.Where(x => x.Reason == input.Reason);
            if (input.Status is { } status) query = query.Where(x => x.Status == status);
            if (input.Before is { } before) query = query.Where(x => x.ReceivedAt <= before);
            if (input.Keyword is { } key)
            {
                var isId = Guid.TryParse(key, out var id);
                var traceId = DistributedEventTrace.FromKeyword(key);
                query = query.Where(x => (isId && x.EventId == id) || (traceId != null && x.TraceId == traceId) || x.CorrelationId == key);
            }
            return new PagedResult<DistributedEventRejectionOutputDto>(await query.LongCountAsync(ct),
                await query.OrderByDescending(x => x.Id).Skip(input.Offset).Take(input.Limit).Select(DistributedEventProjections.Rejection).ToListAsync(ct));
        }, cancellationToken);
    }

    public async Task<DistributedEventOutboxDetailOutputDto> GetOutboxAsync(long id, string? database = null, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(database, false, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) =>
        {
            var query = Outbox(db, target).Where(x => x.Id == id);
            var record = await query.Select(DistributedEventProjections.Outbox).SingleOrDefaultAsync(ct) ?? throw NotFound();
            return new DistributedEventOutboxDetailOutputDto { Record = record, Headers = await query.Select(x => x.Headers).SingleAsync(ct) };
        }, cancellationToken);
    }

    public async Task<DistributedEventInboxDetailOutputDto> GetInboxAsync(long id, string? database = null, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(database, false, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) =>
        {
            var query = Inbox(db, target).Where(x => x.Id == id);
            var record = await query.Select(DistributedEventProjections.Inbox).SingleOrDefaultAsync(ct) ?? throw NotFound();
            var metadata = await query.Select(x => new { x.Headers, x.Coordinates }).SingleAsync(ct);
            return new DistributedEventInboxDetailOutputDto { Record = record, Headers = metadata.Headers, Coordinates = metadata.Coordinates };
        }, cancellationToken);
    }

    public async Task<DistributedEventRejectionDetailOutputDto> GetRejectionAsync(long id, string? database = null, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(database, true, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) =>
        {
            var query = db.Set<DistributedEventRejection>().AsNoTracking().Where(x => x.Id == id);
            var record = await query.Select(DistributedEventProjections.Rejection).SingleOrDefaultAsync(ct) ?? throw NotFound();
            var metadata = await query.Select(x => new { x.Headers, x.Coordinates }).SingleAsync(ct);
            return new DistributedEventRejectionDetailOutputDto { Record = record, Headers = metadata.Headers, Coordinates = metadata.Coordinates };
        }, cancellationToken);
    }

    public async Task<DistributedEventPayloadOutputDto> GetOutboxPayloadAsync(long id, string? database = null, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(database, false, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) => new DistributedEventPayloadOutputDto
        { Payload = await Outbox(db, target).Where(x => x.Id == id).Select(x => x.Payload).SingleOrDefaultAsync(ct) ?? throw NotFound() }, cancellationToken);
    }
    public async Task<DistributedEventPayloadOutputDto> GetInboxPayloadAsync(long id, string? database = null, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(database, false, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) => new DistributedEventPayloadOutputDto
        { Payload = await Inbox(db, target).Where(x => x.Id == id).Select(x => x.Payload).SingleOrDefaultAsync(ct) ?? throw NotFound() }, cancellationToken);
    }
    public async Task<DistributedEventRawMessageOutputDto> GetRejectionPayloadAsync(long id, string? database = null, CancellationToken cancellationToken = default)
    {
        var target = await scope.ResolveAsync(database, true, cancellationToken);
        return await scope.RunAsync(target, async (db, ct) => new DistributedEventRawMessageOutputDto
        { RawBody = await db.Set<DistributedEventRejection>().Where(x => x.Id == id).Select(x => x.RawBody).SingleOrDefaultAsync(ct) ?? throw NotFound() }, cancellationToken);
    }

    public Task<DistributedEventRecoveryOutputDto> RedeliverAsync(RecoverDistributedEventOutboxInputDto input, string? database = null, CancellationToken cancellationToken = default)
        => recovery.OutboxAsync(input, database, cancellationToken);
    public Task<DistributedEventRecoveryOutputDto> ReplayAsync(RecoverDistributedEventInboxInputDto input, string? database = null, CancellationToken cancellationToken = default)
        => recovery.InboxAsync(input, database, cancellationToken);
    public Task<DistributedEventRecoveryOutputDto> RedecodeAsync(RecoverDistributedEventRejectionInputDto input, string? database = null, CancellationToken cancellationToken = default)
        => recovery.RejectionsAsync(input, database, false, cancellationToken);
    public Task<DistributedEventRecoveryOutputDto> DiscardAsync(RecoverDistributedEventRejectionInputDto input, string? database = null, CancellationToken cancellationToken = default)
        => recovery.RejectionsAsync(input, database, true, cancellationToken);

    public async Task<DistributedEventTestOutputDto> SendTestEventAsync(string? database = null, CancellationToken cancellationToken = default)
    {
        if (tenant.Id is not null && database is not null)
            throw new BusinessException(DistributedEventErrorCodes.HostOnly, "Only the host can select a database.");
        if (database is not (null or "host"))
            throw new System.ComponentModel.DataAnnotations.ValidationException(new System.ComponentModel.DataAnnotations.ValidationResult(
                "Test events cannot be published into a selected tenant database.", ["Database"]), null, database);
        if (!options.CurrentValue.TestEvent.Enabled)
            throw new BusinessException(DistributedEventErrorCodes.TestEventDisabled, "Test events are disabled.");
        using var unit = units.Begin();
        var message = new DistributedEventTestEvent();
        await publisher.PublishAsync(message, cancellationToken);
        await unit.CompleteAsync(cancellationToken);
        return new() { EventId = message.EventId };
    }

    internal static IQueryable<DistributedEventOutbox> Outbox(TDbContext db, DistributedEventDatabaseTarget target)
        => target.AllTenants ? db.Set<DistributedEventOutbox>().IgnoreQueryFilters() : db.Set<DistributedEventOutbox>().IgnoreQueryFilters().Where(x => x.TenantId == target.TenantId);
    internal static IQueryable<DistributedEventInbox> Inbox(TDbContext db, DistributedEventDatabaseTarget target)
        => target.AllTenants ? db.Set<DistributedEventInbox>().IgnoreQueryFilters() : db.Set<DistributedEventInbox>().IgnoreQueryFilters().Where(x => x.TenantId == target.TenantId);
    internal static BusinessException NotFound() => new(DistributedEventErrorCodes.RecordNotFound, "The distributed event record does not exist.");
}
