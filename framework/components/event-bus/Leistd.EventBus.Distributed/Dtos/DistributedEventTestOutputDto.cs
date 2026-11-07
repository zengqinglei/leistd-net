namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>测试发布结果；按原事件标识查询发件箱与收件箱。</summary>
public sealed record DistributedEventTestOutputDto
{
    /// <summary>原事件标识。</summary>
    public required Guid EventId { get; init; }
}
