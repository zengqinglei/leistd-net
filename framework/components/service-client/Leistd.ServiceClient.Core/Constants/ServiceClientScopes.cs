namespace Leistd.ServiceClient.Constants;

/// <summary>
/// 服务间调用约定的 OAuth2 scope。
/// </summary>
public static class ServiceClientScopes
{
    /// <summary>
    /// 用户委托 scope：允许该客户端「代表某个用户」调用下游服务，
    /// 即其携带的 <c>X-User-*</c> 头会被被调方采信并恢复为用户主体。
    /// </summary>
    /// <remarks>
    /// 这是<b>委托能力</b>的开关，与"能拿到 client_credentials 令牌"相互独立——
    /// 不区分两者时，任何持有机器令牌的客户端只要知道用户 Id 就能冒充该用户。
    /// 被调方默认要求该 scope（fail-closed），由认证服务在客户端注册时按需授予。
    /// </remarks>
    public const string Delegation = "svc.delegate";
}
