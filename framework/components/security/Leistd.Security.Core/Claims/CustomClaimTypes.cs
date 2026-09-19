namespace Leistd.Security.Claims;

/// <summary>
/// 定义 Leistd 使用的自定义声明类型。
/// </summary>
/// <remarks>
/// 仅包含与 System.Security.Claims.ClaimTypes 不同的自定义字段。
/// 标准字段请直接使用 System.Security.Claims.ClaimTypes。
/// </remarks>
public static class CustomClaimTypes
{
    /// <summary>
    /// 主体标识（OIDC <c>sub</c>）。
    /// </summary>
    /// <remarks>
    /// 自然人主体是 GUID 用户 Id；机器主体按 <see cref="ClientSubject"/> 带 <c>client:</c> 前缀；
    /// 后台作业等非请求入口由宿主自行约定前缀。<c>ICurrentUser.Id</c> 只在能解析成 GUID 时有值，
    /// 因此需要"任何主体都留得下标识"的场景（审计）应读本 claim 的原始值。
    /// </remarks>
    public const string Subject = "sub";

    /// <summary>
    /// OAuth2/OIDC 客户端标识符。
    /// </summary>
    public const string ClientId = "client_id";

    /// <summary>
    /// 表示 OIDC 会话标识符。
    /// </summary>
    public const string SessionId = "sid";

    /// <summary>
    /// 表示身份提供者。
    /// </summary>
    public const string IdentityProvider = "idp";

    /// <summary>
    /// 是否超级管理员（值: "true" / "false"）
    /// </summary>
    /// <remarks>
    /// 由认证端在签发主体时写入，便于授权策略基于 claim 判定而无需查库。
    /// </remarks>
    public const string IsSuperAdmin = "is_super_admin";

    /// <summary>
    /// 所属租户 Id（值: 租户 Guid 字符串；宿主用户无此 claim）
    /// </summary>
    /// <remarks>
    /// 由认证端在签发主体时写入。多租户解析链的 Claim 贡献者以它为最高优先来源——
    /// 已认证用户的租户由此定案，请求头与查询串无法改写。
    /// </remarks>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// 模拟登录时的<b>真实</b>操作人标识（值: 用户 Guid 字符串；非模拟场景无此 claim）
    /// </summary>
    /// <remarks>
    /// 宿主管理员以租户身份操作时，主体上的用户是<b>被模拟者</b>。这个 claim 留住真正按下按钮的人，
    /// 供审计还原"谁做的"——缺了它，事后追责会指向一个什么都没做的租户管理员。
    /// </remarks>
    public const string ImpersonatorUserId = "impersonator_id";

    /// <summary>
    /// 模拟登录时真实操作人的显示名快照（非模拟场景无此 claim）
    /// </summary>
    /// <remarks>存快照而非事后联表取现名：改名或销号之后，审计要回答的仍是"当时是谁"。</remarks>
    public const string ImpersonatorUserName = "impersonator_name";
}
