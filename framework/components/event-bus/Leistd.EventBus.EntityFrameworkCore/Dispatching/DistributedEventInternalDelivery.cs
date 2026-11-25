namespace Leistd.EventBus.EntityFrameworkCore.Dispatching;

// 发件箱内部的持久化路由元数据，不属于跨传输消息头约定；发布时冻结进行里，派发进程不必认识事件类型。
internal static class DistributedEventInternalDelivery
{
    public const string Header = "leistd-delivery";
    public const string Value = "internal";
    public const string Transport = "internal";

    public static bool IsInternal(IReadOnlyDictionary<string, string> headers)
        => headers.TryGetValue(Header, out var value) && value == Value;
}
