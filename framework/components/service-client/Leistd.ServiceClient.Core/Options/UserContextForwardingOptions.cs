using Leistd.ServiceClient.Constants;

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
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否转发用户名（值经 UTF-8 URL 编码）。默认 <c>true</c>。
    /// 用户 Id 在 <see cref="Enabled"/> 时总是转发。
    /// </summary>
    public bool ForwardUsername { get; set; } = true;

    /// <summary>
    /// 用户 Id 的请求头名称。默认 <see cref="ServiceClientHeaders.UserId"/>，须与被调方 <c>ServiceUserContextOptions.UserIdHeader</c> 一致。
    /// </summary>
    public string UserIdHeader { get; set; } = ServiceClientHeaders.UserId;

    /// <summary>
    /// 用户名的请求头名称。默认 <see cref="ServiceClientHeaders.Username"/>，须与被调方 <c>ServiceUserContextOptions.UsernameHeader</c> 一致。
    /// </summary>
    public string UsernameHeader { get; set; } = ServiceClientHeaders.Username;

    /// <summary>
    /// 获取声明类型到请求头名称的附加映射。
    /// </summary>
    /// <remarks>不要转发角色或权限；被调方应自行授权。</remarks>
    public IDictionary<string, string> ClaimHeaderMap { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 获取或设置是否转发当前租户标识。
    /// </summary>
    /// <remarks>默认启用且独立于用户转发；未注册租户上下文时自动跳过。</remarks>
    public bool ForwardTenantId { get; set; } = true;

    /// <summary>
    /// 租户 Id 的请求头名称。默认 <see cref="ServiceClientHeaders.TenantId"/>，须与被调方 <c>ServiceUserContextOptions.TenantIdHeader</c> 一致。
    /// </summary>
    /// <remarks>服务间委托通道：只带租户 GUID、只对受信调用方生效，与匿名请求的租户提示头刻意不同名。</remarks>
    public string TenantIdHeader { get; set; } = ServiceClientHeaders.TenantId;
}
