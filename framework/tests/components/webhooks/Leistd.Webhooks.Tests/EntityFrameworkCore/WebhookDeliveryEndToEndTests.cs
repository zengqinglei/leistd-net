using System.Net;
using Leistd.BackgroundJobs.InProcess;
using Leistd.BackgroundJobs.Queues;
using Leistd.Lock.Memory;
using Leistd.UnitOfWork;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Publishing;
using Leistd.Webhooks.Signing;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>
/// 入口测试（A1–A3）：真实工作单元、进程内队列、默认 HTTP 发送器与本机 Kestrel 接收端，
/// 签名用 Standard Webhooks 官方 C# 库验证。
/// </summary>
public sealed class WebhookDeliveryEndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    private readonly WebhookReceiver _receiver = new();
    private WebhookTestHost _host = null!;
    private IHostedService[] _hostedServices = [];

    public async Task InitializeAsync()
    {
        await _receiver.InitializeAsync();
        _host = await WebhookTestHost.CreateAsync(
            services =>
            {
                services.RemoveAll<IBackgroundTaskQueue>();
                services.RemoveAll<IWebhookSender>();
                services.AddTransient<IWebhookSender, WebhookHttpSender>();
                services.AddInProcessBackgroundJobs();
                services.AddMemoryLocalLock();
            },
            new Dictionary<string, string?>
            {
                // 接收端在回环地址上；周期任务由用例按需手动执行
                ["Leistd:Webhooks:AllowedNetworks:0"] = "127.0.0.0/8",
                ["Leistd:BackgroundJobs:Enabled"] = "false"
            },
            // 官方库按真实时间校验 webhook-timestamp（容差 5 分钟）
            start: DateTimeOffset.UtcNow);

        _hostedServices = [.. _host.Services.GetServices<IHostedService>()];
        foreach (var hostedService in _hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var hostedService in _hostedServices.Reverse())
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        await _host.DisposeAsync();
        await _receiver.DisposeAsync();
    }

    /// <summary>A1：提交后数秒内收到 POST，三个头齐全，官方库验签通过，请求体是发布时的信封。</summary>
    [Fact]
    public async Task A_committed_event_arrives_signed_within_seconds()
    {
        var (_, secret) = await _host.AddSubscriptionAsync(null, _receiver.UrlFor("/ok/a1").ToString());

        await _host.PublishAsync(null, data: new { orderId = 42 });

        var received = await WaitAsync(() => _receiver.Received.TryGetValue("a1", out var webhook) ? webhook : null);
        Assert.True(received.Headers.ContainsKey(WebhookSigner.IdHeader));
        Assert.True(received.Headers.ContainsKey(WebhookSigner.TimestampHeader));
        Assert.True(received.Headers.ContainsKey(WebhookSigner.SignatureHeader));
        new StandardWebhooks.Webhook(secret).Verify(received.Body, ToHeaderCollection(received.Headers));
        Assert.Contains("\"orderId\":42", received.Body, StringComparison.Ordinal);
        Assert.Contains($"\"type\":\"{WebhookTestHost.OrderPaid}\"", received.Body, StringComparison.Ordinal);

        var delivery = await WaitAsync(async () =>
            (await _host.DeliveriesAsync()).SingleOrDefault(x => x.Status == WebhookDeliveryStatus.Succeeded));
        Assert.Equal(received.Headers[WebhookSigner.IdHeader], delivery.EventId.ToString());
    }

    /// <summary>A2：工作单元回滚时接收方收不到请求，也不落事件与投递。</summary>
    [Fact]
    public async Task A_rolled_back_event_is_never_sent()
    {
        await _host.AddSubscriptionAsync(null, _receiver.UrlFor("/ok/a2-rollback").ToString());

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            await scope.ServiceProvider.GetRequiredService<IWebhookPublisher>().PublishAsync(WebhookTestHost.OrderPaid, null);
            await unitOfWork.RollbackAsync();
        }

        // 对照：随后提交的事件到达，说明同一条链路确实在工作
        await _host.AddSubscriptionAsync(null, _receiver.UrlFor("/ok/a2-commit").ToString(), WebhookTestHost.OrderShipped);
        await _host.PublishAsync(null, WebhookTestHost.OrderShipped);
        await WaitAsync(() => _receiver.Received.TryGetValue("a2-commit", out var webhook) ? webhook : null);

        Assert.False(_receiver.Received.ContainsKey("a2-rollback"));
        Assert.Single(await _host.DeliveriesAsync());
    }

    /// <summary>A3：先回 500、到了重试时刻再回 200；两次尝试都有记录，webhook-id 相同。</summary>
    [Fact]
    public async Task A_retry_after_a_500_succeeds_with_the_same_webhook_id()
    {
        var (_, secret) = await _host.AddSubscriptionAsync(null, _receiver.UrlFor("/flaky/a3").ToString());
        await _host.PublishAsync(null);

        var failed = await WaitAsync(async () =>
            (await _host.DeliveriesAsync()).SingleOrDefault(x => x.AttemptCount == 1));
        Assert.Equal(WebhookDeliveryStatus.Pending, failed.Status);
        Assert.Equal((int)HttpStatusCode.InternalServerError, failed.LastStatusCode);

        _host.Clock.Advance(TimeSpan.FromSeconds(6));
        await _host.RunDispatchAsync();

        var succeeded = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Succeeded, succeeded.Status);
        Assert.Equal(2, (await _host.AttemptsAsync(succeeded.Id)).Count);

        var requests = _receiver.History["a3"].ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(requests[0].Headers[WebhookSigner.IdHeader], requests[1].Headers[WebhookSigner.IdHeader]);
        new StandardWebhooks.Webhook(secret).Verify(requests[1].Body, ToHeaderCollection(requests[1].Headers));
    }

    private static WebHeaderCollection ToHeaderCollection(IReadOnlyDictionary<string, string> headers)
    {
        var collection = new WebHeaderCollection();
        foreach (var (name, value) in headers)
        {
            collection[name] = value;
        }

        return collection;
    }

    private static Task<T> WaitAsync<T>(Func<T?> probe) where T : class
        => WaitAsync(() => Task.FromResult(probe()));

    private static async Task<T> WaitAsync<T>(Func<Task<T?>> probe) where T : class
    {
        using var timeout = new CancellationTokenSource(ReceiveTimeout);
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Nothing observed within {ReceiveTimeout}.");
            }

            await Task.Delay(50, CancellationToken.None);
        }
    }
}
