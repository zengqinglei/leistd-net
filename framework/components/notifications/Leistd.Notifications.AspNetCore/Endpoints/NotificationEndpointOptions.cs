namespace Leistd.Notifications.AspNetCore.Endpoints;

/// <summary>
/// 通知中心端点的授权口径。
/// </summary>
/// <remarks>
/// 策略名必填，组件不内置默认策略、也不在路由组上套宿主默认策略：那会把"宿主眼中的默认主体"
/// 悄悄叠到每个端点上。漏配时 <c>MapNotifications</c> 在映射时就抛出。
/// </remarks>
public sealed class NotificationEndpointOptions
{
    /// <summary>
    /// 访问通知中心所需的授权策略名。
    /// </summary>
    /// <remarks>全部端点只作用于当前用户自己的通知，宿主通常给一条"已登录的交互用户"策略。</remarks>
    public string AccessPolicy { get; set; } = string.Empty;

    // 缺必填项抛 ArgumentException，ParamName 即属性名
    internal void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(AccessPolicy);
}
