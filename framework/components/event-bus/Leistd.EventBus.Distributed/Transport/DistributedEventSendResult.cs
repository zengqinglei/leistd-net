namespace Leistd.EventBus.Distributed.Transport;

/// <summary>发送结果；失败必须给稳定错误码，错误文本用于诊断。</summary>
public sealed record DistributedEventSendResult
{
    /// <summary>确认或失败分类。</summary>
    public required DistributedEventSendStatus Status { get; init; }

    /// <summary>失败错误码，成功时为空。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>已脱敏的失败原因，不得含凭据、连接串或载荷；框架保存时截断到 512 个字符。</summary>
    public string? Error { get; init; }
}
