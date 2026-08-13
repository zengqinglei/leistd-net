#if (IncludeTenancy)
using Leistd.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 租户会话自恢复：已认证会话命中"租户不存在/已停用"时，注销 Cookie 并让请求得以恢复，
/// 避免用户被死锁在无法访问任何页面（包括登录页与注销端点）的状态。
/// </summary>
/// <remarks>
/// <para>已认证主体的租户由 claim 定案（解析链首位），租户被删除/停用后这份 Cookie/Bearer
/// 所代表的租户身份已不再成立——不注销的话，后续每个请求（含加载 SPA 的 HTML 导航）都会
/// 在多租户中间件处被拒，用户连回到登录页的路都没有。</para>
/// <para>响应形状：浏览器 HTML 导航（GET + Accept 含 text/html）注销后重定向回原地址——
/// 下一次请求已是匿名，SPA 正常加载并由前端守卫送去登录页；XHR/API 请求返回 401，
/// 前端把它当会话失效清理本地状态。此处按 Accept 区分是**恢复路径**的定向处理，
/// 与"认证拒绝一律回状态码"的既有决策不冲突——那条针对的是权限语义，这里是会话已不成立。</para>
/// <para>匿名请求的租户异常不经此处（继续 404/403）：登录前选错租户名理应看到明确错误。</para>
/// </remarks>
public static class TenantSessionRecoveryMiddleware
{
    /// <summary>
    /// 挂载恢复中间件。置于 <c>UseAuthentication()</c> 之后、<c>UseMultiTenancy()</c> 之前
    /// </summary>
    public static IApplicationBuilder UseTenantSessionRecovery(this IApplicationBuilder app, string cookieScheme)
    {
        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (System.Exception ex) when (
                ex is TenantNotFoundException or TenantNotActiveException &&
                context.User.Identity?.IsAuthenticated == true &&
                !context.Response.HasStarted)
            {
                // 会话所属租户已不可用：注销 Cookie（Bearer 无服务端注销，仅回 401）
                await context.SignOutAsync(cookieScheme);

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
        });
    }
}
#endif
