namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>仅 Manage 策略可读取的 JSON 载荷或原始字节。</summary>
public sealed record DistributedEventPayloadOutputDto
{
    /// <summary>发件箱与收件箱的冻结 JSON。</summary>
    public string? Payload { get; init; }

    /// <summary>拒收的原始字节，JSON 为 Base64 字符串。</summary>
    public byte[]? RawBody { get; init; }
}
