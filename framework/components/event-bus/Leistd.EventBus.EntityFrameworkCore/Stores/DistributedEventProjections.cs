using System.Linq.Expressions;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.EntityFrameworkCore.Entities;

namespace Leistd.EventBus.EntityFrameworkCore.Stores;

internal static class DistributedEventProjections
{
    public static readonly Expression<Func<DistributedEventOutbox, DistributedEventOutboxOutputDto>> Outbox = row => new()
    {
        Id = row.Id,
        EventId = row.EventId,
        EventName = row.EventName,
        TenantId = row.TenantId,
        OrderingKey = row.OrderingKey,
        OccurredAt = row.OccurredAt,
        AttemptCount = row.AttemptCount,
        NextAttemptAt = row.NextAttemptAt,
        LastErrorCode = row.LastErrorCode,
        LastError = row.LastError,
        LastModificationTime = row.LastModificationTime,
        Status = row.Status,
        RedeliverCount = row.RedeliverCount,
        CreationTime = row.CreationTime,
        FirstFailureAt = row.FirstFailureAt,
        SentAt = row.SentAt,
    };

    public static readonly Expression<Func<DistributedEventInbox, DistributedEventInboxOutputDto>> Inbox = row => new()
    {
        Id = row.Id,
        EventId = row.EventId,
        EventName = row.EventName,
        TenantId = row.TenantId,
        OrderingKey = row.OrderingKey,
        OccurredAt = row.OccurredAt,
        AttemptCount = row.AttemptCount,
        NextAttemptAt = row.NextAttemptAt,
        LastErrorCode = row.LastErrorCode,
        LastError = row.LastError,
        LastModificationTime = row.LastModificationTime,
        Status = row.Status,
        Consumer = row.Consumer,
        DuplicateCount = row.DuplicateCount,
        ReplayCount = row.ReplayCount,
        ReceivedAt = row.ReceivedAt,
        ProcessedAt = row.ProcessedAt,
    };

    public static readonly Expression<Func<DistributedEventRejection, DistributedEventRejectionOutputDto>> Rejection = row => new()
    {
        Id = row.Id,
        Transport = row.Transport,
        Reason = row.Reason,
        Error = row.Error,
        EventId = row.EventId,
        EventName = row.EventName,
        Status = row.Status,
        RedecodeCount = row.RedecodeCount,
        ReceivedAt = row.ReceivedAt,
        ResolvedAt = row.ResolvedAt,
        LastModificationTime = row.LastModificationTime,
    };

}
