namespace Leistd.EventBus.Distributed.Errors;

/// <summary>单条或勾选恢复未改动记录的原因。</summary>
public static class DistributedEventRecoverySkipReasons
{
    /// <summary>记录不存在或不在当前查看范围内。</summary>
    public const string NotFound = "not-found";

    /// <summary>当前状态不允许执行请求动作。</summary>
    public const string InvalidState = "invalid-state";

    /// <summary>使用当前传输重新解码仍然失败。</summary>
    public const string RedecodeFailed = "redecode-failed";
}
