namespace Leistd.RealTime.Options;

/// <summary>
/// 实时（RealTime）配置。
/// </summary>
/// <remarks>
/// 只承载实时组件自己的东西。心跳、超时、详细错误属于 SignalR 自身的 <c>HubOptions</c>，
/// 用 <c>AddSignalR(o =&gt; ...)</c> 配；连接主体的 claim 解析属于
/// <c>Leistd.AspNetCore.SignalR</c> 的 <c>HubIdentityOptions</c>。
/// </remarks>
public class RealTimeOptions
{
    /// <summary>业务事件 Hub 路径。</summary>
    public string RealTimeHubPath { get; set; } = "/hubs/realtime";
}
