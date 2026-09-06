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
}
