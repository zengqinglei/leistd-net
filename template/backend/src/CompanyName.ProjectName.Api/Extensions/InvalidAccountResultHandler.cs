#if (IncludeIdentity)
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 把"凭据背后的账号已失效"这一种授权失败改判为 401，其余一切按默认处理。
/// </summary>
/// <remarks>
/// 403 的含义是"身份成立，但没有这个权限"；账号被删除、禁用或锁定时，这份 Cookie/Bearer
/// 代表的身份本身已经不成立，那是 401。区别不是措辞：前端只把 401 当作会话失效来清理本地
/// 认证状态与权限缓存，返回 403 会让被禁用的用户在界面上一直保持"已登录"，刷新页面时
/// 启动流也只会进故障分支而不登出。
///
/// 反过来，不能让前端把 403 一概当作登出——那样一次正常的权限不足就会把用户踢下线。
/// 所以区分必须发生在服务端，且只针对这一种失败：机器令牌进人类管理端点仍然是 403。
///
/// 只做响应形状的定向映射，判定逻辑仍然只有 ActiveUserHandler 一处；
/// 不需要安全戳、令牌黑名单或分布式会话中心。
/// </remarks>
public sealed class InvalidAccountResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var invalidAccount = authorizeResult.AuthorizationFailure?.FailureReasons
            .Any(reason => reason.Message == ActiveUserRequirement.InvalidAccountReason) == true;

        if (invalidAccount)
        {
            // 直接写状态码，不转交 ChallengeAsync：每个认证方案对 challenge 有自己的解释，
            // OpenIddict Validation 见令牌语法有效就答 403——它无从知道令牌背后的账号已经没了。
            // "这份凭据已经失效"是本应用的判断，就由本应用表达，且与其余拒绝路径保持同一形状：
            // 只有状态码，没有重定向也没有响应体。
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
#endif
