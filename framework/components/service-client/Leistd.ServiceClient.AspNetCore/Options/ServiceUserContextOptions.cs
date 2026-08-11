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
    public bool Enable { get; set; } = true;

    /// <summary>
    /// 用户 Id 头名。默认 <see cref="ServiceClientHeaders.UserId"/>。
    /// </summary>
    public string UserIdHeader { get; set; } = ServiceClientHeaders.UserId;

    /// <summary>
    /// 用户名头名（值经 UTF-8 URL 编码）。默认 <see cref="ServiceClientHeaders.UserName"/>。
    /// </summary>
    public string UserNameHeader { get; set; } = ServiceClientHeaders.UserName;

    /// <summary>
    /// 额外的请求头 → claim 映射（key 为头名，value 为 claim 类型），
    /// 与调用方 <c>UserContextForwardingOptions.ClaimHeaderMap</c> 相对应。默认空。
    /// </summary>
    public IDictionary<string, string> HeaderClaimMap { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 调用方不受信时是否从请求中剥离上述用户头，阻断伪造链路。默认 <c>true</c>。
    /// </summary>
    public bool RemoveUntrustedHeaders { get; set; } = true;

    /// <summary>
    /// 额外要求调用方 token 必须包含的 scope（可空）。
    /// 同时识别标准 <c>scope</c>（空格分隔）与 OpenIddict 的 <c>oi_scp</c> claim。
    /// </summary>
    public string? RequiredScope { get; set; }

    /// <summary>
    /// 恢复出的用户身份的 AuthenticationType，用于区分「经服务头恢复」与直接认证的用户。
    /// 默认 <c>ServiceUserContext</c>。
    /// </summary>
    public string AuthenticationType { get; set; } = "ServiceUserContext";
}
