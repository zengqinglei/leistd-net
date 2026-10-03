using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>
/// 浏览器写 API 请求与实时 Hub 连接必须来自本源或部署显式允许的前端源：有 Origin 时按 CORS 策略比对；
/// 没有 Origin 时，Fetch Metadata 标明跨源（cross-site / same-site）的同样拒绝；same-origin、none
/// 与两个头都缺（非浏览器调用）放行。
/// </summary>
/// <remarks>
/// Hub 覆盖全部方法（含 WebSocket 握手的 GET）：CORS 不约束 WebSocket，带 Cookie 的跨站握手（CSWSH）
/// 能读取推送给该用户的数据。Hub 不因带 Authorization 头而跳过检查——浏览器页面可以随手附上这个头。
/// </remarks>
public sealed class BrowserOriginMiddleware(RequestDelegate next, IOptions<CorsOptions> cors, ILogger<BrowserOriginMiddleware> logger)
{
    private readonly CorsPolicy? _policy = cors.Value.GetPolicy(cors.Value.DefaultPolicyName);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var apiWrite = request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(request.Method) &&
            !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method) &&
            !request.Headers.ContainsKey("Authorization");
        if (apiWrite || request.Path.StartsWithSegments("/hubs"))
        {
            var sameOrigin = $"{request.Scheme}://{request.Host}";
            var hasOrigin = request.Headers.TryGetValue("Origin", out var origin);
            var site = request.Headers["Sec-Fetch-Site"];
            var rejected = hasOrigin
                ? origin.Count != 1 || !string.Equals(origin[0], sameOrigin, StringComparison.OrdinalIgnoreCase) &&
                    _policy?.IsOriginAllowed(origin[0]!) != true
                : site.Any(value => value is "cross-site" or "same-site");
            if (rejected)
            {
                logger.LogWarning("Browser request rejected: {Method} {Path}, Origin {Origin}, Sec-Fetch-Site {FetchSite}, request origin {RequestOrigin}.",
                    request.Method, request.Path.Value, origin.ToString(), site.ToString(), sameOrigin);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                // 宿主的 API 状态码管道统一补写 Problem Details。
                return;
            }
        }
        await next(context);
    }
}
