namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>恢复结果；按条件分批提交后返回累计条数。</summary>
public sealed record DistributedEventRecoveryOutputDto
{
    /// <summary>实际改动的记录数。</summary>
    public required long AffectedCount { get; init; }

    /// <summary>未改动的标识和原因；按条件不返回不存在项。</summary>
    public required IReadOnlyList<DistributedEventRecoverySkipDto> Skipped { get; init; }
}
