namespace Leistd.MultiTenancy.EntityFrameworkCore.ConnectionStrings;

/// <summary>
/// 本地解析（宿主直连控制库）的配置。
/// </summary>
public sealed class LocalTenantConnectionOptions
{
    /// <summary>
    /// 获取或设置控制库上下文的连接名（其 <c>[ConnectionStringName]</c> 的值），如 <c>IdentityControl</c>。
    /// </summary>
    /// <remarks>
    /// 必填，且不能是 <c>Default</c>：控制库固定在宿主连接上、不参与租户路由。
    /// 该名称解析为 <c>ConnectionStrings:{名称}</c>，未配置时回落 <c>ConnectionStrings:Default</c>。
    /// </remarks>
    public string? ControlPlaneConnectionStringName { get; set; }
}
