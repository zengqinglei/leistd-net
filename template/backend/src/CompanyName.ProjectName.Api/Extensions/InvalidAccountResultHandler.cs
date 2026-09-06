#if (LocalIdentity)
#if (OpenIddictServer)
using Microsoft.AspNetCore.Authentication;
#endif
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 把"凭据背后的账号已失效"这一种授权失败改判为 401，其余一切按默认处理。
/// </summary>
/// <remarks>
/// 删除、禁用或锁定的账号不再构成有效身份，因此返回 401，供客户端清理会话状态；
/// 已认证主体权限不足仍返回 403。只有 <see cref="ActiveUserHandler"/> 标记的账号失效
/// 会触发此映射，机器主体进入自然人端点等普通授权失败继续走默认处理器。
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
#if (OpenIddictServer)
            // challenge 依据实际认证结果选择，避免请求头与成功认证方案不一致时误判。
            // ASP.NET Core 会按方案复用本次请求的认证结果。
            var bearer = await context.AuthenticateAsync(
                OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
#endif

            // 直接写状态码，不转交 ChallengeAsync：每个认证方案对 challenge 有自己的解释，
            // 令牌校验环节见语法有效就答 403——它无从知道令牌背后的账号已经没了。
            // "这份凭据已经失效"是本应用的判断，就由本应用表达。
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

#if (OpenIddictServer)
            // RFC 9110 对 401 的 WWW-Authenticate 是 MUST，OAuth 客户端也据此把响应识别为
            // "令牌失效、去重新取"而不是一个普通业务错误。
            // Cookie 认证的请求保持裸 401——表单登录没有对应的 HTTP 认证方案名，硬造一个没有意义。
            if (bearer.Succeeded)
            {
                context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            }
#endif

            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
#endif
