using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Leistd.Webhooks.Tests.TestDoubles;

/// <summary>监听 127.0.0.1 随机端口的 Kestrel 接收端，按路径给出不同响应并记录收到的请求。</summary>
/// <remarks>用真实套接字：重定向、超时与连接期拦截都发生在 HttpClient 的连接层，内存 TestServer 绕过它们。</remarks>
public sealed class WebhookReceiver : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>按路径记录收到的请求。</summary>
    public ConcurrentDictionary<string, ReceivedWebhook> Received { get; } = new();

    /// <summary>按路径记录 <c>/flaky/{key}</c> 收到的全部请求，按到达顺序。</summary>
    public ConcurrentDictionary<string, ConcurrentQueue<ReceivedWebhook>> History { get; } = new();

    /// <summary>接收端根地址（<c>http://127.0.0.1:端口</c>）。</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>取接收端上某个路径的绝对地址。</summary>
    public Uri UrlFor(string path) => new(BaseAddress, path);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.Urls.Add("http://127.0.0.1:0");

        _app.MapPost("/ok/{key}", async (string key, HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            Received[key] = new ReceivedWebhook(
                request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                body);
            return Results.Text(new string('x', 4096));
        });
        // 第一次回 500、之后回 200：验证按退避重试时两次尝试是同一个 webhook-id
        _app.MapPost("/flaky/{key}", async (string key, HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            var history = History.GetOrAdd(key, _ => new ConcurrentQueue<ReceivedWebhook>());
            history.Enqueue(new ReceivedWebhook(
                request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                body));
            return history.Count == 1 ? Results.StatusCode(StatusCodes.Status500InternalServerError) : Results.Ok();
        });
        _app.MapPost("/redirect/{key}", (string key) => Results.Redirect($"/ok/{key}"));
        _app.MapPost("/slow", async (CancellationToken cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Results.Ok();
        });
        _app.MapPost("/throttled", (HttpResponse response) =>
        {
            response.Headers.RetryAfter = "120";
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        });

        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        BaseAddress = new Uri(address);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

/// <summary>接收端收到的一次请求。</summary>
/// <param name="Headers">请求头。</param>
/// <param name="Body">请求体原文。</param>
public sealed record ReceivedWebhook(IReadOnlyDictionary<string, string> Headers, string Body);
