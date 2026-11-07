namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>详情概要；没有管理权限也可读取，载荷不随之返回。</summary>
public sealed record DistributedEventRejectionDetailOutputDto
{
    /// <summary>与列表相同的概要。</summary>
    public required DistributedEventRejectionOutputDto Record { get; init; }

    /// <summary>冻结的消息头。</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>不透明的传输坐标。</summary>
    public required IReadOnlyDictionary<string, string> Coordinates { get; init; }
}
