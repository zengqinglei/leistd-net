namespace Leistd.RealTime.AspNetCore.SignalR;

// 资源组名的唯一生成处。订阅侧与发布侧必须逐字一致，分开写必然漂移，
// 而漂移的表现是"推送成功但没人收到"——静默。
//
// 刻意**不**把租户拼进组名。看起来那是一层深度防御，实际方向反了：
// 它防的是"授权器写错导致跨租户订阅"（需要一个 bug 才发生），
// 代价却是"从非请求上下文发布时推到错误的组、事件静默丢失"——
// 后台作业给某租户推送是合法场景，正确的代码就会踩。
// 租户隔离由 IRealTimeSubscriptionAuthorizer 承担：Hub 调用的环境上下文
// 已由 Leistd.AspNetCore.SignalR 的过滤器建立，授权器可直接读 ICurrentTenant。
internal static class RealTimeGroups
{
    internal static string Resource(string resourceKey) => $"resource:{resourceKey}";
}
