using System.Collections.Concurrent;
using Leistd.MultiTenancy.Context;
using Leistd.Webhooks.Delivery;

namespace Leistd.Webhooks.Tests.TestDoubles;

/// <summary>不发网络请求的发送器：按脚本返回结果，并记录每次请求与发送时的租户上下文。</summary>
public sealed class ScriptedWebhookSender(ICurrentTenantAccessor tenantAccessor) : IWebhookSender
{
    private readonly ConcurrentQueue<Func<WebhookSendRequest, WebhookSendResult>> _script = new();

    /// <summary>按发送顺序记录的请求。</summary>
    public ConcurrentQueue<SentWebhook> Sent { get; } = new();

    /// <summary>脚本用完后的默认结果，初始为 200。</summary>
    public Func<WebhookSendRequest, WebhookSendResult> Default { get; set; } = Status(200);

    /// <summary>为接下来的请求依次排定结果。</summary>
    public void Enqueue(params Func<WebhookSendRequest, WebhookSendResult>[] results)
    {
        foreach (var result in results)
        {
            _script.Enqueue(result);
        }
    }

    /// <inheritdoc />
    public Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(new SentWebhook(request, tenantAccessor.Current?.TenantId));
        var respond = _script.TryDequeue(out var next) ? next : Default;
        return Task.FromResult(respond(request));
    }

    /// <summary>构造一个收到响应的结果；请求头快照取订阅的自定义头加 <c>webhook-id</c>。</summary>
    public static Func<WebhookSendRequest, WebhookSendResult> Status(int statusCode, TimeSpan? retryAfter = null)
        => request => Respond(statusCode, request, retryAfter);

    /// <summary>构造一个没有收到响应的结果（连接失败）。</summary>
    public static Func<WebhookSendRequest, WebhookSendResult> ConnectionFailure(string error)
        => request => new WebhookSendResult
        {
            Duration = TimeSpan.FromMilliseconds(5),
            RequestHeaders = Snapshot(request),
            Error = error
        };

    private static WebhookSendResult Respond(int statusCode, WebhookSendRequest request, TimeSpan? retryAfter) => new()
    {
        StatusCode = statusCode,
        Duration = TimeSpan.FromMilliseconds(12),
        RequestHeaders = Snapshot(request),
        Response = "ok",
        RetryAfter = retryAfter
    };

    private static Dictionary<string, string> Snapshot(WebhookSendRequest request)
        => new(request.Headers, StringComparer.OrdinalIgnoreCase) { ["webhook-id"] = request.EventId.ToString() };
}

/// <summary>一次被记录的发送。</summary>
/// <param name="Request">请求内容。</param>
/// <param name="TenantId">发送时的当前租户。</param>
public sealed record SentWebhook(WebhookSendRequest Request, Guid? TenantId);
