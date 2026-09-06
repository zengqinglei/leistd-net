using System.Security.Claims;

namespace Leistd.AspNetCore.SignalR.Options;

/// <summary>
/// Hub 连接主体的解析与有效性复检配置。
/// </summary>
public sealed class HubIdentityOptions
{
    /// <summary>
    /// 每次 Hub 调用前复评的授权策略名；为 <see langword="null"/> 时使用宿主的默认策略。
    /// </summary>
    /// <remarks>
    /// 复评的目的是让"账号被禁用/锁定"对<b>已建立的连接</b>生效。ASP.NET Core 只在
    /// 握手那次 HTTP 请求上跑端点策略，此后连接一直有效；宿主的默认策略里通常已经
    /// 有账号有效性要求（如 <c>ActiveUserRequirement</c>），复评即复用同一份判定，
    /// 不引入第二处定义。
    /// </remarks>
    public string? PolicyName { get; set; }

    /// <summary>
    /// 两次复评之间的最小间隔；为 <see langword="null"/>（默认）时每次调用都复评。
    /// </summary>
    /// <remarks>
    /// 默认不节流：Hub 调用频率远低于 HTTP 请求，而账号有效性判定通常是一次主键查询。
    /// 高频 Hub（如光标同步）再按实测放宽。
    /// </remarks>
    public TimeSpan? RevalidationInterval { get; set; }

    /// <summary>
    /// 按顺序解析 SignalR <c>UserIdentifier</c> 的声明类型，取第一个非空值。
    /// </summary>
    /// <remarks>
    /// 显式设为空集合即表示不解析用户标识，此时按用户寻址的推送（<c>Clients.User</c>）全部落空。
    /// </remarks>
    public IReadOnlyList<string> UserIdClaimTypes { get; set; } = ["sub", ClaimTypes.NameIdentifier];
}
