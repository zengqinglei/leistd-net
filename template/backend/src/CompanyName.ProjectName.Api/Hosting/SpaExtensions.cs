using System.Net.Sockets;
using CompanyName.ProjectName.Api.Options;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Net.Http.Headers;
using Polly;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>
/// SPA 的静态文件缓存、静态回退与开发期代理。
/// </summary>
public static class SpaExtensions
{
    private const string HttpClientName = "SpaProxy";

    private const string FingerprintedCacheControl = "public, max-age=31536000, immutable";
    private const string RevalidateCacheControl = "no-cache";

    /// <summary>
    /// HTTP/2 禁止的 hop-by-hop 头（RFC 7540 §8.1.2.2）
    /// Kestrel 在 HTTP/2 响应中自动剥离这些头时会产生大量 WRN 日志
    /// </summary>
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Transfer-Encoding",
        "Keep-Alive",
        "Upgrade",
        "Proxy-Connection"
    };

    public static IServiceCollection AddMyProjectSpaProxy(this IServiceCollection services)
    {
        // SPA 代理转发前端 dev server（vite）的大量并发静态资源请求时，dev server 偶发关闭
        // 连接（SocketException 10053），导致单次转发失败、Angular 无法 bootstrap。按 .NET 官方
        // 弹性处理最佳实践（Microsoft.Extensions.Http.Resilience）挂一个针对“连接类瞬时故障”
        // 的重试管道：只重试网络层异常（HttpRequestException/IOException/SocketException），
        // 不依赖业务幂等（dev 资源转发均为可重放的 GET）。生产关闭 SpaProxy，不受影响。
        services.AddHttpClient(HttpClientName)
            // 与 YARP 转发器的默认处理器一致：原样转发到本机 dev server。不走系统代理——开发机常设
            // HTTP_PROXY，本机请求被交给代理后连不回 localhost，整页 502；不自动跟随重定向、不接管 Cookie，
            // 这两者都应原样交还浏览器。
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                UseCookies = false
            })
            .AddResilienceHandler("spa-proxy", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(50),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(IsTransientConnectionFailure(args.Outcome.Exception))
                });
                builder.AddTimeout(TimeSpan.FromSeconds(30));
            });
        return services;
    }

    private static bool IsTransientConnectionFailure(Exception? ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            IOException => true,
            SocketException => true,
            _ => ex?.InnerException is not null && IsTransientConnectionFailure(ex.InnerException)
        };
    }

    /// <summary>
    /// 前端静态文件的托管选项：按文件是否带内容哈希设置 Cache-Control。
    /// </summary>
    /// <remarks>
    /// <para>不设 Cache-Control 时，浏览器按 Last-Modified 自行推算新鲜期，发版后会继续用旧的
    /// index.html 与词条文件，直到推算的期限过去。</para>
    /// <para>按目录约定判断，不按文件名猜哈希（Angular 的哈希长度与字符集并不固定）：
    /// 构建产物（根目录的 .js/.css 与 media/）在 outputHashing=all 下文件名随内容变化，缓存一年；
    /// index.html 与 public/ 下原样拷贝的文件（i18n/、images/、favicon 等）文件名不变，
    /// 用 no-cache 每次校验，未变化时只回 304。所以 public/ 根目录不要放 .js/.css。</para>
    /// </remarks>
    public static StaticFileOptions CreateSpaStaticFileOptions() => new()
    {
        OnPrepareResponse = context =>
            context.Context.Response.Headers[HeaderNames.CacheControl] =
                GetSpaCacheControl(context.Context.Request.Path, context.File.Name)
    };

    private static string GetSpaCacheControl(PathString requestPath, string fileName)
    {
        // SPA 回退时请求路径是前端路由，实际发出的是 index.html
        if (string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            return RevalidateCacheControl;
        }

        if (requestPath.StartsWithSegments("/media"))
        {
            return FingerprintedCacheControl;
        }

        var path = requestPath.Value ?? string.Empty;
        var isRootLevel = path.LastIndexOf('/') == 0;
        var isBundle = path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        return isRootLevel && isBundle ? FingerprintedCacheControl : RevalidateCacheControl;
    }

    public static IEndpointRouteBuilder MapMyProjectSpaFallback(this WebApplication app)
    {
        app.Map("api", ReturnApiNotFoundAsync).WithDisplayName("ApiNotFoundFallback");
        app.Map("api/{**path}", ReturnApiNotFoundAsync).WithDisplayName("ApiNotFoundFallback");

        var options = app.Configuration.GetSection(SpaProxyOptions.SectionName).Get<SpaProxyOptions>() ?? new SpaProxyOptions();
        if (options.Enabled && !string.IsNullOrWhiteSpace(options.Target))
        {
            var target = new Uri(options.Target);
            app.Map("{**path}", context => ProxySpaDevServerAsync(context, target))
               .WithDisplayName("SpaProxyFallback");
        }
        else
        {
            app.MapFallbackToFile("index.html", CreateSpaStaticFileOptions());
        }

        return app;
    }

    private static Task ReturnApiNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private static async Task ProxySpaDevServerAsync(HttpContext context, Uri target)
    {
        // WebSocket CONNECT 等非标准方法无法通过 HttpClient 代理
        // 优雅降级：返回 404 而非抛异常导致 500/503
        if (HttpMethods.IsConnect(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var requestUri = new UriBuilder(target)
        {
            Path = context.Request.Path,
            Query = context.Request.QueryString.Value
        }.Uri;

        using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), requestUri);
        foreach (var header in context.Request.Headers)
        {
            if (!string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase) &&
                !HopByHopHeaders.Contains(header.Key))
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            requestMessage.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
            {
                requestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        requestMessage.Headers.Host = target.IsDefaultPort ? target.Host : $"{target.Host}:{target.Port}";

        var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        using var responseMessage = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);

        context.Response.StatusCode = (int)responseMessage.StatusCode;

        // 复制响应头，过滤 hop-by-hop 头以避免 HTTP/2 协议冲突警告
        foreach (var header in responseMessage.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        await responseMessage.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
}
