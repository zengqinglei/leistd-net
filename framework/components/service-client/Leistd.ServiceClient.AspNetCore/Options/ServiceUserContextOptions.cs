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
    /// 租户 Id 头名。默认 <see cref="ServiceClientHeaders.TenantId"/>。
    /// 受信服务调用时恢复为 <c>tenant_id</c> claim，交由多租户解析链的 Claim 贡献者定案；
    /// 置空字符串可关闭租户头恢复。不受信来源的该头不剥离（解析链主体优先级已使其无害，
    /// 且匿名登录的租户选择依赖它）。
    /// </summary>
    public string TenantIdHeader { get; set; } = ServiceClientHeaders.TenantId;

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
    /// 机器主体 <c>sub</c> 的命名空间前缀。默认 <c>client:</c>。
    /// </summary>
    /// <remarks>
    /// 受信判定接受两种 client credentials 主体形态：<c>sub == client_id</c>（裸形态）
    /// 或 <c>sub == 前缀 + client_id</c>。签发端给机器主体加前缀是为了与自然人主体
    /// （GUID 形态的用户 Id）隔离命名空间，阻断"把 client_id 取成某个用户 Id 冒充该用户"
    /// 的路径；本模板的 OpenIddict 签发端即采用 <c>client:</c> 前缀。置空只认裸形态。
    /// </remarks>
    public string ClientSubjectPrefix { get; set; } = "client:";

    /// <summary>
    /// 恢复出的用户身份的 AuthenticationType，用于区分「经服务头恢复」与直接认证的用户。
    /// 默认 <c>ServiceUserContext</c>。
    /// </summary>
    public string AuthenticationType { get; set; } = "ServiceUserContext";
}
