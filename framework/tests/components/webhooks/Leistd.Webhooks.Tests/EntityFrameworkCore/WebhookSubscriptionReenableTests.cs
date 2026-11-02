using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Tests.TestDoubles;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>
/// A4b 的重新启用部分：启用清零失败起点；带 <c>retryFailed</c> 时该订阅现存的全部失败投递各生成一次手动尝试，
/// 包括"先创建、后首次失败"的投递与停用时被终结、从未发出请求的投递。
/// </summary>
/// <remarks>按创建时间或失败区间挑选失败投递时，停用前就已创建的那条会被漏掉，管理员以为补发了其实没有。</remarks>
public sealed class WebhookSubscriptionReenableTests : IAsyncLifetime
{
    // 只留一次自动重试，第二次失败即成为终态
    private static readonly Dictionary<string, string?> Settings = new() { ["Leistd:Webhooks:RetrySchedule:0"] = "00:00:05" };

    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync(settings: Settings);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Reenabling_with_retryFailed_retries_every_failed_delivery_once_manually()
    {
        var (subscription, failedEarly, terminated) = await ArrangeDisabledSubscriptionAsync();

        var enabled = await _host.ManageAsync(null, service => service.EnableSubscriptionAsync(subscription, retryFailed: true));

        Assert.True(enabled.IsEnabled);
        Assert.Null(enabled.DisabledReason);
        Assert.Null(enabled.DisabledAt);
        Assert.Null(enabled.FirstFailureAt);
        var scheduled = await _host.DeliveriesAsync();
        Assert.All(scheduled, x => Assert.Equal((WebhookDeliveryStatus.Pending, WebhookAttemptTrigger.Manual), (x.Status, x.NextTrigger)));
        Assert.Equal(2, _host.Queue.Count);

        var sentBefore = _host.Sender.Sent.Count;
        await _host.Queue.RunAllAsync(_host.Services);

        Assert.Equal(sentBefore + 2, _host.Sender.Sent.Count);
        Assert.All(await _host.DeliveriesAsync(), x => Assert.Equal(WebhookDeliveryStatus.Succeeded, x.Status));
        Assert.Equal(WebhookAttemptTrigger.Manual, (await _host.AttemptsAsync(failedEarly)).Last().Trigger);
        Assert.Equal(WebhookAttemptTrigger.Manual, Assert.Single(await _host.AttemptsAsync(terminated)).Trigger);
    }

    /// <summary>不带 <c>retryFailed</c> 只恢复订阅：失败起点清零，失败投递原样保留，不入队。</summary>
    [Fact]
    public async Task Reenabling_without_retryFailed_leaves_failed_deliveries_alone()
    {
        var (subscription, _, _) = await ArrangeDisabledSubscriptionAsync();

        var enabled = await _host.ManageAsync(null, service => service.EnableSubscriptionAsync(subscription));

        Assert.True(enabled.IsEnabled);
        Assert.Null((await _host.SubscriptionAsync(subscription)).FirstFailureAt);
        Assert.All(await _host.DeliveriesAsync(), x => Assert.Equal(WebhookDeliveryStatus.Failed, x.Status));
        Assert.Equal(0, _host.Queue.Count);
    }

    // 一条先创建、之后自动尝试两次都失败的投递；一条在手动停用时被终结、从未发出请求的投递。
    private async Task<(Guid Subscription, Guid FailedEarly, Guid Terminated)> ArrangeDisabledSubscriptionAsync()
    {
        var created = await _host.ManageAsync(null, service => service.CreateSubscriptionAsync(new CreateWebhookSubscriptionInputDto
        {
            Name = "receiver",
            Url = "https://receiver.example/hook",
            EventTypes = [WebhookTestHost.OrderPaid]
        }));
        var subscription = created.Subscription.Id;

        await _host.PublishAsync(null);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(500), ScriptedWebhookSender.Status(500));
        await _host.Queue.RunAllAsync(_host.Services);
        _host.Clock.Advance(TimeSpan.FromSeconds(6));
        await _host.RunDispatchAsync();
        var failedEarly = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Failed, failedEarly.Status);
        Assert.True(failedEarly.CreationTime < (await _host.SubscriptionAsync(subscription)).FirstFailureAt);

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.PublishAsync(null);
        _host.Queue.Drop();
        await _host.ManageAsync(null, service => service.DisableSubscriptionAsync(subscription));

        var terminated = (await _host.DeliveriesAsync()).Single(x => x.Id != failedEarly.Id);
        Assert.Equal(WebhookDeliveryStatus.Failed, terminated.Status);
        Assert.Empty(await _host.AttemptsAsync(terminated.Id));
        Assert.Equal(WebhookDisabledReason.Manual, (await _host.SubscriptionAsync(subscription)).DisabledReason);
        return (subscription, failedEarly.Id, terminated.Id);
    }
}
