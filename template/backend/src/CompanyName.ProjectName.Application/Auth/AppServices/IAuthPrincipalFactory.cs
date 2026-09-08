#if (LocalIdentity)
using System.Security.Claims;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 对外身份材料装配：签发用的主体，以及 userinfo 端点的 claim 集合。
/// </summary>
/// <remarks>
/// 两个方法都按用户 ID 取用户，把「解析用户」与「此刻是否仍允许访问」收在这一处：
/// 这段判断曾在三个协议端点里各写一份，改一处漏两处不会有任何编译或测试信号。
/// 取不到（或签发场景下不允许访问）时返回 <see langword="null"/>，
/// 回什么由调用方按协议决定——授权与令牌端点回 Forbid，userinfo 回 Challenge。
/// </remarks>
public interface IAuthPrincipalFactory
{
    /// <summary>装配签发用的主体；用户不存在或此刻不允许访问时返回 <see langword="null"/>。</summary>
    Task<ClaimsPrincipal?> CreateAsync(
        Guid userId,
        IEnumerable<string>? scopes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 装配 userinfo 端点的 claim 集合；用户不存在时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="userId">令牌 <c>sub</c> 中的用户 ID。</param>
    /// <param name="tokenPrincipal">本次请求的令牌主体，决定按哪些 scope 投影、角色取自哪里。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IDictionary<string, object>?> CreateUserInfoAsync(
        Guid userId,
        ClaimsPrincipal tokenPrincipal,
        CancellationToken cancellationToken = default);
}
#endif
