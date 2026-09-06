#if (!LocalIdentity && IncludeNotifications)
namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>
/// 仅为浏览器 SignalR Hub 将 <c>access_token</c> query 提升为 Bearer 请求头。
/// </summary>
/// <remarks>
/// WebSocket 和 SSE 的浏览器 API 不允许设置 Authorization 头，这是 SignalR 官方客户端的传递方式。
/// 接受面严格限定在模板映射的两个 Hub；普通 API 仍只接受 Authorization 头。
/// 提升后立即从应用内的 QueryString 移除 token，边缘代理仍必须对 access_token 参数做日志脱敏。
/// </remarks>
public sealed class HubAccessTokenMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isHub = context.Request.Path.StartsWithSegments("/hubs/notifications") ||
                    context.Request.Path.StartsWithSegments("/hubs/realtime");
        var values = context.Request.Query["access_token"];

        if (isHub &&
            !context.Request.Headers.ContainsKey("Authorization") &&
            values.Count == 1 &&
            !string.IsNullOrWhiteSpace(values[0]))
        {
            context.Request.Headers.Authorization = $"Bearer {values[0]}";
            context.Request.QueryString = QueryString.Create(
                context.Request.Query
                    .Where(item => !string.Equals(item.Key, "access_token", StringComparison.Ordinal))
                    .SelectMany(item => item.Value.Select(value =>
                        new KeyValuePair<string, string?>(item.Key, value))));
        }

        await next(context);
    }
}

public static class HubAccessTokenApplicationBuilderExtensions
{
    public static IApplicationBuilder UseHubAccessToken(this IApplicationBuilder app) =>
        app.UseMiddleware<HubAccessTokenMiddleware>();
}
#endif
