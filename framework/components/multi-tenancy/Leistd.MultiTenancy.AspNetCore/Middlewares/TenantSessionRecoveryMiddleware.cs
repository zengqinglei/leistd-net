using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.MultiTenancy.Exceptions;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;

namespace Leistd.MultiTenancy.AspNetCore.Middlewares;

// 已认证的租户会话命中"租户不存在/已停用"时注销会话并让请求得以恢复，
// 避免用户被死锁在连登录页与注销端点都访问不了的状态。匿名请求与宿主会话保留原始错误。
internal sealed class TenantSessionRecoveryMiddleware(
    RequestDelegate next,
    TenantSessionRecoveryOptions options,
    ILogger<TenantSessionRecoveryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (
            exception is TenantNotFoundException or TenantNotActiveException &&
            context.User.Identity?.IsAuthenticated == true &&
            context.User.FindFirst(CustomClaimTypes.TenantId) is not null &&
            !context.Response.HasStarted)
        {
            if (options.SignOutScheme is { } scheme)
            {
                await context.SignOutAsync(scheme);
            }

            // 注销的成因是"租户没了"而不是"会话过期"，日志里必须能分开
            logger.LogInformation(
                "Recovered a session whose tenant is no longer available ({Reason}) on {Path}",
                exception.GetType().Name,
                context.Request.Path);

            context.Response.Headers[options.TenantInvalidHeader] = "1";

            var isHtmlNavigation =
                HttpMethods.IsGet(context.Request.Method) &&
                context.Request.Headers.Accept.Any(value =>
                    value != null && value.Contains("text/html", StringComparison.OrdinalIgnoreCase));

            if (isHtmlNavigation)
            {
                // 注销后重定向回原地址：下一次请求已匿名，前端正常加载并送去登录页
                context.Response.Redirect(context.Request.GetEncodedPathAndQuery());
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
        }
    }
}
