using System.Net;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Signing;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Webhooks.Tests;

/// <summary>默认发送器经 <c>AddWebhooks</c> 组合后，对真实 Kestrel 接收端的发送行为。</summary>
/// <remarks>接收端只监听 127.0.0.1：放行回环的用例显式配置 <c>AllowedNetworks</c>，未放行的用例据此证明拦截生效。</remarks>
public sealed class WebhookHttpSenderTests(WebhookReceiver receiver) : IClassFixture<WebhookReceiver>
{
    [Fact]
    public async Task Delivered_request_carries_signed_standard_webhook_headers()
    {
        var secret = new WebhookSigner().GenerateSecret();
        var request = Request("ok/headers", secret) with
        {
            // 自定义头不能冒充组件写入的 webhook-id
            Headers = new Dictionary<string, string> { ["X-Tenant"] = "acme", ["webhook-id"] = "spoofed" }
        };

        var result = await CreateSender(allowLoopback: true).SendAsync(request);

        Assert.Equal(200, result.StatusCode);
        var received = receiver.Received["headers"];
        Assert.Equal(request.Body, received.Body);
        Assert.Equal(request.EventId.ToString(), received.Headers["webhook-id"]);
        Assert.Equal(request.Timestamp.ToUnixTimeSeconds().ToString(), received.Headers["webhook-timestamp"]);
        Assert.StartsWith("application/json", received.Headers["Content-Type"], StringComparison.Ordinal);
        Assert.Equal("acme", received.Headers["X-Tenant"]);

        var verifiable = new WebHeaderCollection();
        foreach (var name in new[] { "webhook-id", "webhook-timestamp", "webhook-signature" })
        {
            verifiable[name] = received.Headers[name];
        }

        new StandardWebhooks.Webhook(secret).Verify(received.Body, verifiable);
    }

    /// <summary>快照记录实际发出的头部供排查，但签名不进记录：它与载荷一起足以重放请求。</summary>
    [Fact]
    public async Task Result_reports_the_sent_headers_without_the_signature_and_truncates_the_response()
    {
        var request = Request("ok/snapshot") with { Headers = new Dictionary<string, string> { ["X-Tenant"] = "acme" } };

        var result = await CreateSender(allowLoopback: true).SendAsync(request);

        Assert.Equal(request.EventId.ToString(), result.RequestHeaders["webhook-id"]);
        Assert.Equal("acme", result.RequestHeaders["X-Tenant"]);
        Assert.True(result.RequestHeaders.ContainsKey("Content-Type"));
        Assert.False(result.RequestHeaders.ContainsKey("webhook-signature"));
        Assert.Equal(WebhookSendResult.MaxResponseLength, result.Response!.Length);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Redirects_are_reported_instead_of_followed()
    {
        var result = await CreateSender(allowLoopback: true).SendAsync(Request("redirect/followed"));

        Assert.Equal(302, result.StatusCode);
        Assert.False(receiver.Received.ContainsKey("followed"));
    }

    [Fact]
    public async Task Retry_after_is_read_from_the_response()
    {
        var result = await CreateSender(allowLoopback: true).SendAsync(Request("throttled"));

        Assert.Equal(429, result.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(120), result.RetryAfter);
    }

    [Fact]
    public async Task A_timeout_is_reported_as_a_failed_result()
    {
        var sender = CreateSender(allowLoopback: true, options => options.RequestTimeout = TimeSpan.FromSeconds(1));

        var result = await sender.SendAsync(Request("slow"));

        Assert.Null(result.StatusCode);
        Assert.Contains("timed out", result.Error, StringComparison.Ordinal);
        Assert.InRange(result.Duration, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
    }

    /// <summary>调用方取消不是一次失败的尝试，必须照常抛出，否则停机时会把投递错记为失败。</summary>
    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateSender(allowLoopback: true).SendAsync(Request("slow"), cancellation.Token));
    }

    /// <summary>默认不放行回环地址；失败原因以实际绑定的配置节写出放行键，运维照着改配置即可。</summary>
    [Theory]
    [InlineData(WebhookOptions.SectionName)]
    [InlineData("Ops:Hooks")]
    public async Task Loopback_is_refused_by_default_and_the_error_names_the_allow_list_key(string section)
    {
        var sender = CreateSender(allowLoopback: false, configSectionPath: section);

        var result = await sender.SendAsync(Request("ok/refused"));

        Assert.Null(result.StatusCode);
        Assert.Contains($"{section}:AllowedNetworks", result.Error, StringComparison.Ordinal);
        Assert.False(receiver.Received.ContainsKey("refused"));
    }

    private IWebhookSender CreateSender(
        bool allowLoopback,
        Action<WebhookOptions>? configure = null,
        string configSectionPath = WebhookOptions.SectionName)
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddWebhooks(
            options =>
            {
                if (allowLoopback)
                {
                    options.AllowedNetworks.Add("127.0.0.0/8");
                }

                configure?.Invoke(options);
            },
            configSectionPath);

        return services.BuildServiceProvider().GetRequiredService<IWebhookSender>();
    }

    private WebhookSendRequest Request(string path, string? secret = null) => new()
    {
        EventId = Guid.NewGuid(),
        EventType = "user.created",
        SubscriptionId = Guid.NewGuid(),
        Url = receiver.UrlFor(path),
        Timestamp = DateTimeOffset.UtcNow,
        Body = """{"type":"user.created","timestamp":"2026-11-03T00:00:00Z","data":{"id":1}}""",
        Headers = new Dictionary<string, string>(),
        Secrets = [secret ?? new WebhookSigner().GenerateSecret()]
    };
}
