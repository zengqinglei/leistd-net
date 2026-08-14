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
    /// 这是委托能力的开关，与「能拿到 client_credentials 令牌」相互独立——
    /// 后者只代表调用方是一个已认证的工作负载。若不区分两者，任何获得机器令牌的客户端
    /// （包括只该同步公开数据的第三方集成）只要知道用户 Id 就能冒充该用户，
    /// 继承其角色与直授权限。因此被调方默认要求该 scope（fail-closed），
    /// 由认证服务在客户端注册时按需授予。
    /// </remarks>
    public const string Delegation = "svc.delegate";
}
