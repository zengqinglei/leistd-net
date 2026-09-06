using Leistd.MultiTenancy;
using Leistd.MultiTenancy.AspNetCore;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Leistd.MultiTenancy.Exceptions;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>
/// 租户会话自恢复：已认证会话命中"租户不存在/已停用"时，注销 Cookie 并让请求得以恢复，
/// 避免用户被死锁在无法访问任何页面（包括登录页与注销端点）的状态。
/// </summary>
/// <remarks>
/// 仅处理带 <c>tenant_id</c> claim 的已认证租户会话；匿名请求和宿主会话保留原始错误。
/// 恢复响应带 <c>X-Tenant-Invalid</c>，使客户端只在租户失效时清除租户选择。
/// HTML GET 导航注销后重定向回原地址，由匿名 SPA 流程接管；XHR/API 请求返回 401。
/// Bearer 无服务端会话可注销，但响应形状与 Cookie 会话保持一致。
/// </remarks>
public sealed class TenantSessionRecoveryMiddleware(
    RequestDelegate next,
    string cookieScheme,
    ILogger<TenantSessionRecoveryMiddleware> logger)
{
    /// <summary>
    /// 标记本次 401/重定向源于"会话所属租户已不可用"，供前端清理本地租户选择。
    /// </summary>
    public const string TenantInvalidHeader = "X-Tenant-Invalid";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (System.Exception ex) when (
            ex is TenantNotFoundException or TenantNotActiveException &&
            context.User.Identity?.IsAuthenticated == true &&
            context.User.FindFirst(CustomClaimTypes.TenantId) is not null &&
            !context.Response.HasStarted)
        {
            // 会话所属租户已不可用：注销 Cookie（Bearer 无服务端注销，仅回 401）
            await context.SignOutAsync(cookieScheme);

            // 记一条：注销的成因是"租户没了"而不是"会话过期"，日志里必须能分开。
            // 只看 401 的话，运维无法区分一次租户停用与一次普通超时
            logger.LogInformation(
                "Signed out a session whose tenant is no longer available ({Reason}) on {Path}",
                ex.GetType().Name,
                context.Request.Path);

            // 两条分支都带上：XHR 由拦截器读取，HTML 导航虽走整页加载由启动校验兜底，
            // 但保持同一语义比按分支分叉更容易解释
            context.Response.Headers[TenantInvalidHeader] = "1";

            var isHtmlNavigation =
                HttpMethods.IsGet(context.Request.Method) &&
                context.Request.Headers.Accept.Any(value =>
                    value != null && value.Contains("text/html", StringComparison.OrdinalIgnoreCase));

            if (isHtmlNavigation)
            {
                // 注销后重定向回原地址：下一次请求已匿名，SPA 正常加载并由守卫送去登录页
                context.Response.Redirect(context.Request.GetEncodedPathAndQuery());
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
        }
    }
}

/// <summary>
/// <see cref="TenantSessionRecoveryMiddleware"/> 的管道挂载扩展
/// </summary>
public static class TenantSessionRecoveryApplicationBuilderExtensions
{
    /// <summary>
    /// 挂载恢复中间件。置于 <c>UseAuthentication()</c> 之后、<c>UseMultiTenancy()</c> 之前
    /// </summary>
    public static IApplicationBuilder UseTenantSessionRecovery(this IApplicationBuilder app, string cookieScheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieScheme);
        return app.UseMiddleware<TenantSessionRecoveryMiddleware>(cookieScheme);
    }
}
