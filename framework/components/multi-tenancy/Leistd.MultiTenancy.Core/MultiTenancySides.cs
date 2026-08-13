namespace Leistd.MultiTenancy;

/// <summary>
/// 多租户侧别：一项能力（如权限定义）可声明它属于租户侧、宿主侧或两侧通用
/// </summary>
[Flags]
public enum MultiTenancySides
{
    /// <summary>
    /// 租户侧（当前存在租户上下文）
    /// </summary>
    Tenant = 1,

    /// <summary>
    /// 宿主侧（无租户上下文）
    /// </summary>
    Host = 2,

    /// <summary>
    /// 两侧通用
    /// </summary>
    Both = Tenant | Host
}
