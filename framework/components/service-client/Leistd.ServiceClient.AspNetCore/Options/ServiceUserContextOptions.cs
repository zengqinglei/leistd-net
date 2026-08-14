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
    /// 要求调用方 token 必须包含的委托 scope，默认
    /// <see cref="ServiceClientScopes.Delegation"/>（<c>svc.delegate</c>）。
    /// 同时识别标准 <c>scope</c>（空格分隔）与 OpenIddict 的 <c>oi_scp</c> claim。
    /// </summary>
    /// <remarks>
    /// 默认非空是**安全默认**（fail-closed）：认证成功只说明调用方是已认证的工作负载，
    /// 不等于它有权代表用户。未授予该 scope 的客户端携带 <c>X-User-*</c> 头时，
    /// 头会被剥离、不恢复任何用户主体。认证服务需在客户端注册时显式授予该 scope；
    /// 部署上另有等价管控时可置空关闭该校验，但那意味着任何机器令牌都能代表任意用户。
    /// </remarks>
    public string? RequiredScope { get; set; } = ServiceClientScopes.Delegation;

    /// <summary>
    /// 恢复出的用户身份的 AuthenticationType，用于区分「经服务头恢复」与直接认证的用户。
    /// 默认 <c>ServiceUserContext</c>。
    /// </summary>
    public string AuthenticationType { get; set; } = "ServiceUserContext";
}
