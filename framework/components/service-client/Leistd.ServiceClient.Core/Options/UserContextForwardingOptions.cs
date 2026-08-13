namespace Leistd.ServiceClient.Options;

/// <summary>
/// 用户上下文出站转发配置：控制把当前用户的哪些信息写入出站请求头。
/// </summary>
public class UserContextForwardingOptions
{
    /// <summary>
    /// 是否启用用户上下文转发。默认 <c>true</c>；
    /// 宿主未注册 <c>ICurrentUser</c>（如未调用 <c>AddSecurity()</c>）时自动跳过。
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    /// 是否转发用户名（<c>X-User-Name</c>，值经 UTF-8 URL 编码）。默认 <c>true</c>。
    /// 用户 Id（<c>X-User-Id</c>）在 <see cref="Enable"/> 时总是转发。
    /// </summary>
    public bool ForwardUserName { get; set; } = true;

    /// <summary>
    /// 额外的 claim → 请求头映射（key 为 claim 类型，value 为头名），值经 UTF-8 URL 编码。
    /// 默认空。角色、权限不应经头传递——被调方应基于调用方 client 的 scope 或按用户 Id 本地判定。
    /// </summary>
    public IDictionary<string, string> ClaimHeaderMap { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 是否转发当前租户 Id（<c>X-Tenant-Id</c>，值来自 <c>ICurrentTenant</c> 环境上下文）。
    /// 默认 <c>true</c>；宿主未注册 <c>ICurrentTenant</c>（非多租户宿主）时自动跳过。
    /// 独立于 <see cref="Enable"/>：后台任务可能只有租户上下文而无用户主体。
    /// </summary>
    public bool ForwardTenantId { get; set; } = true;
}
