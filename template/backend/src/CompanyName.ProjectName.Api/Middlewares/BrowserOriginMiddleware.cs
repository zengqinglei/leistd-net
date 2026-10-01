using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>浏览器写 API 请求的 Origin 必须属于本源或部署显式允许的前端源。</summary>
public sealed class BrowserOriginMiddleware(RequestDelegate next, IOptions<CorsOptions> cors, ILogger<BrowserOriginMiddleware> logger)
{
    private readonly CorsPolicy? _policy = cors.Value.GetPolicy(cors.Value.DefaultPolicyName);

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(request.Method) &&
            !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method) &&
            !request.Headers.ContainsKey("Authorization") && request.Headers.TryGetValue("Origin", out var origin))
        {
            var sameOrigin = $"{request.Scheme}://{request.Host}";
            if (origin.Count != 1 || !string.Equals(origin[0], sameOrigin, StringComparison.OrdinalIgnoreCase) &&
                _policy?.IsOriginAllowed(origin[0]!) != true)
            {
                logger.LogWarning("Browser API write rejected: Origin {Origin}, request origin {RequestOrigin}.", origin.ToString(), sameOrigin);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                // 宿主的 API 状态码管道统一补写 Problem Details。
                return;
            }
        }
        await next(context);
    }
}
