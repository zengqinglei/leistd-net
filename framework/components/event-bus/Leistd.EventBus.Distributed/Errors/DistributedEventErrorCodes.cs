namespace Leistd.EventBus.Distributed.Errors;

/// <summary>管理用例的稳定业务错误码。</summary>
public static class DistributedEventErrorCodes
{
    /// <summary>记录不存在。</summary>
    public const string RecordNotFound = "DistributedEvents:RecordNotFound";

    /// <summary>选择的物理库不存在。</summary>
    public const string DatabaseNotFound = "DistributedEvents:DatabaseNotFound";

    /// <summary>当前操作仅允许宿主上下文。</summary>
    public const string HostOnly = "DistributedEvents:HostOnly";

    /// <summary>宿主已关闭内置测试事件。</summary>
    public const string TestEventDisabled = "DistributedEvents:TestEventDisabled";
}
