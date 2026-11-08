namespace Leistd.EventBus.Distributed.Errors;

/// <summary>框架写入收件箱的处理失败码。</summary>
public static class DistributedEventInboxFailureCodes
{
    /// <summary>处理器执行或业务事务提交失败。</summary>
    public const string HandlerException = "handler-exception";
}
