using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Leistd.Security.Claims;

/// <summary>
/// 判定主体是否匿名的唯一规则。
/// </summary>
/// <remarks>
/// 同一主体常合并了多个身份（多个认证方案、服务间还原出的被代表用户与调用方机器身份）。
/// "这个请求是不是匿名"在框架里被问了很多次（当前用户、租户解析、会话恢复、环境上下文、Hub 复评、操作记录），
/// 答案必须一致：一处只看第一个身份、另一处看全部，同一个请求就会一边被当成匿名、一边被当成已认证。
/// </remarks>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// 主体的任一身份已认证时为真；主体为 <see langword="null"/> 或全部身份都未认证时为假。
    /// </summary>
    /// <remarks>
    /// 与官方 <c>DenyAnonymousAuthorizationRequirement</c> 判定"已认证用户"的口径一致。
    /// 不要用 <see cref="ClaimsPrincipal.Identity"/>：它只是第一个身份，首身份未认证、后续身份已认证的主体会被误判为匿名。
    /// 只回答"是否匿名"；"这个人是谁"取自主体身份，见 <see cref="ClaimTypeOptions.FindSubjectIdentity"/>。
    /// </remarks>
    /// <param name="principal">主体。</param>
    /// <returns>是否存在已认证的身份。</returns>
    public static bool HasAuthenticatedIdentity([NotNullWhen(true)] this ClaimsPrincipal? principal)
        => principal?.Identities.Any(identity => identity.IsAuthenticated) == true;
}
