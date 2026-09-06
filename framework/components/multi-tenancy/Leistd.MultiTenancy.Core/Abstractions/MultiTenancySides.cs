namespace Leistd.MultiTenancy.Abstractions;

/// <summary>
/// 指定能力适用的多租户侧别。
/// </summary>
[Flags]
public enum MultiTenancySides
{
    /// <summary>
    /// 表示租户侧。
    /// </summary>
    Tenant = 1,

    /// <summary>
    /// 表示宿主侧。
    /// </summary>
    Host = 2,

    /// <summary>
    /// 表示租户侧和宿主侧。
    /// </summary>
    Both = Tenant | Host
}
