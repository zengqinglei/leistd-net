using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Dispatching;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Tests.TestDoubles;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>签名密钥解密不了（如多副本没有共享 Data Protection 密钥环）时：不发请求，记一次失败尝试，按普通失败退避直至终态。</summary>
/// <remarks>不记尝试的话投递永远停在待投递，每轮扫描重报同一个错误，日志里看不到它何时开始、订阅也不会因连续失败停用。</remarks>
public sealed class WebhookSecretUnavailableTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string?> Settings = new() { ["Leistd:Webhooks:RetrySchedule:0"] = "00:00:05" };

    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync(settings: Settings);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_undecryptable_secret_records_a_failed_attempt_and_follows_the_retry_schedule()
    {
        var subscription = await _host.InTenantAsync(null, (_, db) =>
        {
            var created = new WebhookSubscription
            {
                Name = "receiver",
                Url = "https://receiver.example/hook",
                EventTypes = [WebhookTestHost.OrderPaid],
                Secret = "written-with-another-key-ring",
                CreationTime = _host.Now
            };
            db.Add(created);
            return Task.FromResult(created);
        });
        await _host.PublishAsync(null);

        await _host.Queue.RunAllAsync(_host.Services);

        var rescheduled = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal((WebhookDeliveryStatus.Pending, 1, 1), (rescheduled.Status, rescheduled.AttemptCount, rescheduled.AutoAttemptCount));
        Assert.Equal(WebhookDeliveryProcessor<WebhookTestDbContext>.SecretUnavailableError, rescheduled.LastError);
        var attempt = Assert.Single(await _host.AttemptsAsync(rescheduled.Id));
        Assert.Null(attempt.StatusCode);
        Assert.Equal(WebhookDeliveryProcessor<WebhookTestDbContext>.SecretUnavailableError, attempt.Error);
        Assert.Equal(_host.Now, (await _host.SubscriptionAsync(subscription.Id)).FirstFailureAt);

        _host.Clock.Advance(TimeSpan.FromSeconds(6));
        await _host.RunDispatchAsync();

        var failed = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal((WebhookDeliveryStatus.Failed, 2), (failed.Status, failed.AttemptCount));
        Assert.Null(failed.NextAttemptAt);
        Assert.Empty(_host.Sender.Sent);
    }
}
