using Leistd.ServiceClient.AspNetCore.Claims;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.ServiceClient.AspNetCore.Middlewares;

/// <summary>
/// 被调方用户上下文中间件（置于 <c>UseAuthentication</c> 之后、<c>UseAuthorization</c> 之前）。
/// </summary>
/// <remarks>
/// 两项职责：
/// <list type="number">
/// <item>调用方**不受信**时按配置剥离请求中的 <c>X-User-*</c> 头，阻断伪造链路；</item>
/// <item>受信但认证阶段未恢复（宿主未注册 <see cref="ServiceUserContextClaimsTransformation"/>
/// 的兜底路径）时，恢复 <c>HttpContext.User</c>。</item>
/// </list>
/// 信任边界见 <see cref="ServiceUserContextClaimsTransformation"/>：仅采信已认证 client
/// credentials 主体（<c>sub == client_id</c>）携带的用户头。
/// </remarks>
/// <param name="next">下一个中间件</param>
/// <param name="options">恢复配置</param>
/// <param name="logger">日志</param>
public class ServiceUserContextMiddleware(
    RequestDelegate next,
    IOptions<ServiceUserContextOptions> options,
    ILogger<ServiceUserContextMiddleware> logger)
{
    /// <summary>
    /// 处理请求。
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var opts = options.Value;
        if (!opts.Enable)
        {
            await next(context);
            return;
        }

        if (ServiceUserContext.IsEnriched(context.User, opts))
        {
            // 认证阶段（ClaimsTransformation）已恢复
            await next(context);
            return;
        }

        if (ServiceUserContext.IsTrustedServiceCall(context.User, opts))
        {
            var restored = ServiceUserContext.TryRestore(context.User, context.Request.Headers, opts);
            if (restored is not null)
            {
                context.User = restored;
                logger.LogDebug(
                    "已从服务调用头恢复用户上下文: {UserId}",
                    restored.FindFirst(ServiceUserContext.SubjectClaimType)?.Value);
            }
        }
        else if (opts.RemoveUntrustedHeaders)
        {
            RemoveUserHeaders(context, opts);
        }

        await next(context);
    }

    private static void RemoveUserHeaders(HttpContext context, ServiceUserContextOptions opts)
    {
        context.Request.Headers.Remove(opts.UserIdHeader);
        context.Request.Headers.Remove(opts.UserNameHeader);
        foreach (var headerName in opts.HeaderClaimMap.Keys)
        {
            context.Request.Headers.Remove(headerName);
        }
    }
}
