#if (LocalIdentity && OpenIddictServer)
using Microsoft.AspNetCore.Authentication;
#endif
using Leistd.OperationRecords.AspNetCore.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 授权结果里两处本应用专有的处置：<b>被拒的写操作补一条失败的操作记录</b>；
/// 本地身份形态下，"凭据背后的账号已失效"改判为 401。其余一切按默认处理。
/// </summary>
/// <remarks>
/// <para><b>两件事收在同一个类里，是因为 ASP.NET Core 只认一个
/// <see cref="IAuthorizationMiddlewareResultHandler"/>。</b>框架侧刻意不接管这个扩展点
/// （占住它，宿主唯一的授权处置入口就没了），只提供
/// <c>HttpContext.RecordDeniedOperationAsync()</c> 这个零件，由这里一行调用。</para>
/// <para><b>补记是无条件的。</b><c>[Authorize(Policy = ...)]</c> 的拒绝发生在授权阶段，
/// 请求到不了应用服务，那里的记录调用看不见它——于是"谁在反复尝试他没有的权限"这类问题
/// 没有任何痕迹可查。资源服务形态没有本地账号，但一样有带策略的写端点，
/// 把补记放进本地身份的守卫里会让那半边静默没有审计。</para>
/// </remarks>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
#if (LocalIdentity)
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

            // 账号失效走 401，不是"权限不足"，因此不落被拒记录：这条请求没有一个仍然有效的操作人。
            return;
        }
#endif

        if (authorizeResult.Forbidden)
        {
            // 记不记由端点上的 [OperationRecordAction] 决定；没打注解就什么都不做。
            await context.RecordDeniedOperationAsync();
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
