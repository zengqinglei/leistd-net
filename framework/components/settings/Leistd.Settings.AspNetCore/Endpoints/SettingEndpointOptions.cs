namespace Leistd.Settings.AspNetCore.Endpoints;

/// <summary>设置端点的授权口径。</summary>
/// <remarks>
/// 两个策略名都必填，组件不内置默认策略，也不在路由组上叠加宿主默认策略；漏配任一项时 <c>MapSettings</c> 映射即抛出。
/// </remarks>
public sealed class SettingEndpointOptions
{
    /// <summary>读取设置与写自己偏好所需的授权策略名，通常是“已登录的交互用户”。</summary>
    public string AccessPolicy { get; set; } = string.Empty;

    /// <summary>写入当前租户设置所需的授权策略名。</summary>
    public string TenantWritePolicy { get; set; } = string.Empty;

    // 缺必填项抛 ArgumentException，ParamName 即属性名
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AccessPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(TenantWritePolicy);
    }
}
