using Leistd.ServiceClient.Constants;

namespace Leistd.ServiceClient.AspNetCore.Options;

/// <summary>
/// 被调方用户上下文恢复配置，绑定配置节 <c>Leistd:ServiceUserContext</c>。
/// </summary>
public class ServiceUserContextOptions
{
    /// <summary>
    /// 是否启用。默认 <c>true</c>；<c>false</c> 时中间件直接放行（也不剥离头）。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 用户 Id 头名。默认 <see cref="ServiceClientHeaders.UserId"/>。
    /// </summary>
    public string UserIdHeader { get; set; } = ServiceClientHeaders.UserId;

    /// <summary>
    /// 用户名头名（值经 UTF-8 URL 编码）。默认 <see cref="ServiceClientHeaders.Username"/>。
    /// </summary>
    public string UsernameHeader { get; set; } = ServiceClientHeaders.Username;

    /// <summary>
    /// 获取或设置租户标识请求头名称。
    /// </summary>
    /// <remarks>受信调用会将该值恢复为租户声明；置空可关闭恢复。</remarks>
    public string TenantIdHeader { get; set; } = ServiceClientHeaders.TenantId;

    /// <summary>
    /// 获取请求头名称到声明类型的附加映射。
    /// </summary>
    public IDictionary<string, string> HeaderClaimMap { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 调用方不受信时是否从请求中剥离上述用户头，阻断伪造链路。默认 <c>true</c>。
    /// </summary>
    public bool RemoveUntrustedHeaders { get; set; } = true;

    /// <summary>
    /// 获取或设置调用方令牌必须包含的委托范围。
    /// </summary>
    /// <remarks>
    /// 默认为 <see cref="ServiceClientScopes.Delegation"/>。置空会允许任意已认证工作负载恢复用户身份。
    /// </remarks>
    public string? RequiredScope { get; set; } = ServiceClientScopes.Delegation;

    /// <summary>
    /// 恢复出的用户身份的 AuthenticationType，用于区分「经服务头恢复」与直接认证的用户。
    /// 默认 <c>ServiceUserContext</c>。
    /// </summary>
    public string AuthenticationType { get; set; } = "ServiceUserContext";
}
