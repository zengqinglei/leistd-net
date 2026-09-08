namespace Leistd.RealTime.AspNetCore.SignalR;

// 资源组名的唯一生成处。订阅侧与发布侧必须逐字一致，分开写必然漂移，
// 而漂移的表现是"推送成功但没人收到"——静默。
//
// 组名不依赖 ambient 租户，使后台发布与订阅使用相同寻址规则。
// 租户隔离由 IRealTimeSubscriptionAuthorizer 在已建立的调用上下文内判定。
internal static class RealTimeGroups
{
    internal static string Resource(string resourceKey) => $"resource:{resourceKey}";
}
