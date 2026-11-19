using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Stores;

namespace Leistd.EventBus.Distributed.Management;

// 非 HTTP 消费方经过同一输入校验；租户与物理库边界由持久化适配器在打开工作单元前建立。
internal sealed class DistributedEventManagementService(IDistributedEventManagementStore store) : IDistributedEventManagementService
{
    public Task<DistributedEventStatusOutputDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return store.GetStatusAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DistributedEventCatalogOutputDto>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return store.GetCatalogAsync(cancellationToken);
    }

    public Task<PagedResult<DistributedEventOutboxOutputDto>> GetOutboxListAsync(GetDistributedEventOutboxInputDto input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        return store.GetOutboxListAsync(input, cancellationToken);
    }

    public Task<DistributedEventOutboxDetailOutputDto> GetOutboxAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ValidationException(new ValidationResult("Id must be positive.", ["Id"]), null, id);
        return store.GetOutboxAsync(id, cancellationToken);
    }

    public Task<DistributedEventPayloadOutputDto> GetOutboxPayloadAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ValidationException(new ValidationResult("Id must be positive.", ["Id"]), null, id);
        return store.GetOutboxPayloadAsync(id, cancellationToken);
    }

    public Task<PagedResult<DistributedEventInboxOutputDto>> GetInboxListAsync(GetDistributedEventInboxInputDto input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        return store.GetInboxListAsync(input, cancellationToken);
    }

    public Task<DistributedEventInboxDetailOutputDto> GetInboxAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ValidationException(new ValidationResult("Id must be positive.", ["Id"]), null, id);
        return store.GetInboxAsync(id, cancellationToken);
    }

    public Task<DistributedEventPayloadOutputDto> GetInboxPayloadAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ValidationException(new ValidationResult("Id must be positive.", ["Id"]), null, id);
        return store.GetInboxPayloadAsync(id, cancellationToken);
    }

    public Task<PagedResult<DistributedEventRejectionOutputDto>> GetRejectionsAsync(GetDistributedEventRejectionsInputDto input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        return store.GetRejectionsAsync(input, cancellationToken);
    }

    public Task<DistributedEventRejectionDetailOutputDto> GetRejectionAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ValidationException(new ValidationResult("Id must be positive.", ["Id"]), null, id);
        return store.GetRejectionAsync(id, cancellationToken);
    }

    public Task<DistributedEventRawMessageOutputDto> GetRejectionPayloadAsync(long id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ValidationException(new ValidationResult("Id must be positive.", ["Id"]), null, id);
        return store.GetRejectionPayloadAsync(id, cancellationToken);
    }

    public Task<DistributedEventRecoveryOutputDto> RedeliverAsync(RecoverDistributedEventOutboxInputDto input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        return store.RedeliverAsync(input, cancellationToken);
    }

    public Task<DistributedEventRecoveryOutputDto> ReplayAsync(RecoverDistributedEventInboxInputDto input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        return store.ReplayAsync(input, cancellationToken);
    }

    public Task<DistributedEventRecoveryOutputDto> RedecodeAsync(RecoverDistributedEventRejectionInputDto input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        ValidateRejectionState(input.Filter?.Status, false);
        return store.RedecodeAsync(input, cancellationToken);
    }

    public Task<DistributedEventRecoveryOutputDto> DiscardAsync(RecoverDistributedEventRejectionInputDto input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        ValidateRejectionState(input.Filter?.Status, true);
        return store.DiscardAsync(input, cancellationToken);
    }

    public Task<DistributedEventTestOutputDto> SendTestEventAsync(CancellationToken cancellationToken = default)
    {
        return store.SendTestEventAsync(cancellationToken);
    }

    private static void ValidateRejectionState(DistributedEventRejectionStatus? status, bool discard)
    {
        if (status is not null && status != DistributedEventRejectionStatus.Pending
            && !(discard && status == DistributedEventRejectionStatus.RedecodePending))
            throw new ValidationException(new ValidationResult("The rejection state is not valid for this action.", ["Status"]), null, status);
    }

    private static void Validate(object input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        switch (input)
        {
            case RecoverDistributedEventOutboxInputDto value: ValidateRecovery(value.Ids, value.Filter); break;
            case RecoverDistributedEventInboxInputDto value: ValidateRecovery(value.Ids, value.Filter); break;
            case RecoverDistributedEventRejectionInputDto value: ValidateRecovery(value.Ids, value.Filter); break;
            case GetDistributedEventOutboxInputDto value: ValidateTimes(value.Before, value.StartTime, value.EndTime); break;
            case GetDistributedEventInboxInputDto value: ValidateTimes(value.Before, value.StartTime, value.EndTime); break;
            case GetDistributedEventRejectionsInputDto value: ValidateTimes(value.Before, value.StartTime, value.EndTime); break;
        }
    }

    private static void ValidateRecovery(IReadOnlyList<long>? ids, object? filter)
    {
        if ((ids is null) == (filter is null))
            throw new ValidationException(new ValidationResult("Specify either Ids or Filter.", ["Ids", "Filter"]), null, null);
        if (ids is not null && (ids.Count is < 1 or > 100 || ids.Any(x => x <= 0)))
            throw new ValidationException(new ValidationResult("Ids must contain 1 to 100 positive identifiers.", ["Ids"]), null, ids);
        if (filter is null) return;
        Validator.ValidateObject(filter, new ValidationContext(filter), validateAllProperties: true);
        if (filter is DistributedEventInboxFilterDto inbox && inbox.Status is not (DistributedEventInboxStatus.Failed or DistributedEventInboxStatus.Processed))
            throw new ValidationException(new ValidationResult("Status must be failed or processed.", ["Filter.Status"]), null, inbox.Status);
        switch (filter)
        {
            case DistributedEventOutboxFilterDto value: ValidateTimes(value.Before, value.StartTime, value.EndTime); break;
            case DistributedEventInboxFilterDto value: ValidateTimes(value.Before, value.StartTime, value.EndTime); break;
            case DistributedEventRejectionFilterDto value: ValidateTimes(value.Before, value.StartTime, value.EndTime); break;
        }
    }

    private static void ValidateTimes(DateTime? before, DateTime? startTime, DateTime? endTime)
    {
        RequireUtc(before, "Before");
        RequireUtc(startTime, "StartTime");
        RequireUtc(endTime, "EndTime");
        if (startTime > endTime)
            throw new ValidationException(new ValidationResult("StartTime must not be later than EndTime.", ["StartTime", "EndTime"]), null, startTime);
    }

    private static void RequireUtc(DateTime? value, string member)
    {
        if (value is { Kind: not DateTimeKind.Utc })
            throw new ValidationException(new ValidationResult($"{member} must be UTC.", [member]), null, value);
    }
}
