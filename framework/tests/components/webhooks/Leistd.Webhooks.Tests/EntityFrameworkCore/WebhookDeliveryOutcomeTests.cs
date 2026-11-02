using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Dispatching;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.EntityFrameworkCore.Subscriptions;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>投递处理按结果更新投递与订阅：重试排期、410 与连续失败停用、停用时终结待投递、密钥轮换、请求头快照。</summary>
public sealed class WebhookDeliveryOutcomeTests : IAsyncLifetime
{
    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>失败按退避表排下一次（首项 5 秒 ±10%），自动次数加一；之后成功即成功，并清零订阅的失败起点。</summary>
    [Fact]
    public async Task A_failure_is_rescheduled_and_a_later_success_clears_the_first_failure()
    {
        var (subscription, _) = await _host.AddSubscriptionAsync(null);
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(500));
        await _host.PublishAsync(null);

        await _host.Queue.RunAllAsync(_host.Services);

        var failed = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Pending, failed.Status);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal(1, failed.AutoAttemptCount);
        Assert.Equal(500, failed.LastStatusCode);
        Assert.InRange(failed.NextAttemptAt!.Value, _host.Now.AddSeconds(4.5), _host.Now.AddSeconds(5.5));
        Assert.Equal(_host.Now, (await _host.SubscriptionAsync(subscription.Id)).FirstFailureAt);

        _host.Clock.Advance(TimeSpan.FromSeconds(6));
        await _host.RunDispatchAsync();

        var succeeded = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Succeeded, succeeded.Status);
        Assert.Null(succeeded.NextAttemptAt);
        Assert.Equal(2, succeeded.AttemptCount);
        Assert.Null((await _host.SubscriptionAsync(subscription.Id)).FirstFailureAt);
    }

    /// <summary>
    /// A4、A4b：410 让投递失败、订阅以"端点已注销"停用并发出停用事件；同一事务里该订阅的其余待投递（含已入队的）被终结，
    /// 之后队列项与兜底扫描都不再为它发请求。
    /// </summary>
    [Fact]
    public async Task A_410_disables_the_subscription_and_terminates_its_other_pending_deliveries()
    {
        var (subscription, _) = await _host.AddSubscriptionAsync(null);
        await _host.PublishAsync(null);
        await _host.PublishAsync(null);
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(410));

        // 第一条的队列项发出请求收到 410；第二条的队列项随后执行
        await _host.Queue.RunAllAsync(_host.Services);
        _host.Clock.Advance(TimeSpan.FromMinutes(2));
        await _host.RunDispatchAsync();

        Assert.Single(_host.Sender.Sent);
        var stored = await _host.SubscriptionAsync(subscription.Id);
        Assert.False(stored.IsEnabled);
        Assert.Equal(WebhookDisabledReason.EndpointGone, stored.DisabledReason);
        Assert.Equal(_host.Now.AddMinutes(-2), stored.DisabledAt);

        var deliveries = await _host.DeliveriesAsync();
        Assert.All(deliveries, x => Assert.Equal(WebhookDeliveryStatus.Failed, x.Status));
        Assert.All(deliveries, x => Assert.Null(x.NextAttemptAt));
        Assert.Equal(410, deliveries[0].LastStatusCode);
        Assert.Equal(WebhookSubscriptionDeactivator<WebhookTestDbContext>.DescribeDisabled(WebhookDisabledReason.EndpointGone), deliveries[1].LastError);
        Assert.Empty(await _host.AttemptsAsync(deliveries[1].Id));

        var disabled = Assert.IsType<WebhookSubscriptionDisabledEvent>(Assert.Single(_host.Events.Published));
        Assert.Equal(subscription.Id, disabled.SubscriptionId);
        Assert.Equal(WebhookDisabledReason.EndpointGone, disabled.Reason);
    }

    /// <summary>A4b：发送入口遇到已停用的订阅不发请求，按停用原因终结投递；订阅被删除时同样终结。</summary>
    [Fact]
    public async Task The_send_entry_terminates_deliveries_of_a_disabled_subscription_without_sending()
    {
        var (subscription, _) = await _host.AddSubscriptionAsync(null);
        await _host.PublishAsync(null);
        await _host.InTenantAsync(null, async (provider, db) =>
        {
            var tracked = await db.Set<WebhookSubscription>().SingleAsync(x => x.Id == subscription.Id);
            tracked.IsEnabled = false;
            tracked.DisabledReason = WebhookDisabledReason.Manual;
        });

        await _host.Queue.RunAllAsync(_host.Services);

        Assert.Empty(_host.Sender.Sent);
        var delivery = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(WebhookSubscriptionDeactivator<WebhookTestDbContext>.DescribeDisabled(WebhookDisabledReason.Manual), delivery.LastError);
        Assert.Equal(0, delivery.AttemptCount);
    }

    /// <summary>
    /// A4b：经停用服务手动停用时，同一事务把该订阅的待投递全部终结并发停用事件；别的订阅的待投递不受影响。
    /// 这类从未发出请求的投递保持失败、可被之后的重新启用选中重发。
    /// </summary>
    [Fact]
    public async Task Disabling_manually_terminates_only_that_subscriptions_pending_deliveries()
    {
        var (target, _) = await _host.AddSubscriptionAsync(null);
        var (other, _) = await _host.AddSubscriptionAsync(null);
        await _host.PublishAsync(null);

        await _host.InTenantAsync(null, async (provider, db) =>
        {
            var tracked = await db.Set<WebhookSubscription>().SingleAsync(x => x.Id == target.Id);
            await provider.GetRequiredService<WebhookSubscriptionDeactivator<WebhookTestDbContext>>()
                .DisableAsync(tracked, WebhookDisabledReason.Manual, CancellationToken.None);
        });

        // 停用的同一事务里已终结，不靠之后的发送入口兜底
        var terminated = (await _host.DeliveriesAsync()).Single(x => x.SubscriptionId == target.Id);
        Assert.Equal(WebhookDeliveryStatus.Failed, terminated.Status);
        Assert.Null(terminated.NextAttemptAt);
        Assert.Equal(WebhookSubscriptionDeactivator<WebhookTestDbContext>.DescribeDisabled(WebhookDisabledReason.Manual), terminated.LastError);

        await _host.Queue.RunAllAsync(_host.Services);

        var sent = Assert.Single(_host.Sender.Sent);
        Assert.Equal(other.Id, sent.Request.SubscriptionId);
        var deliveries = (await _host.DeliveriesAsync()).ToDictionary(x => x.SubscriptionId);
        Assert.Equal(WebhookDeliveryStatus.Failed, deliveries[target.Id].Status);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, deliveries[other.Id].Status);
        Assert.Equal(WebhookDisabledReason.Manual, Assert.IsType<WebhookSubscriptionDisabledEvent>(Assert.Single(_host.Events.Published)).Reason);
    }

    /// <summary>A5：从首次失败起超过 AutoDisableAfter（5 天）仍失败时自动停用；未超过时只排重试。</summary>
    [Fact]
    public async Task Failing_beyond_AutoDisableAfter_disables_the_subscription()
    {
        var (subscription, _) = await _host.AddSubscriptionAsync(null);
        _host.Sender.Default = ScriptedWebhookSender.ConnectionFailure("Connection refused.");
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);
        var firstFailure = _host.Now;

        _host.Clock.Advance(TimeSpan.FromDays(5));
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);
        Assert.True((await _host.SubscriptionAsync(subscription.Id)).IsEnabled);

        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);

        var stored = await _host.SubscriptionAsync(subscription.Id);
        Assert.False(stored.IsEnabled);
        Assert.Equal(WebhookDisabledReason.ConsecutiveFailures, stored.DisabledReason);
        Assert.Equal(firstFailure, stored.FirstFailureAt);
        // 停用在同一事务里终结了刚排了重试的这条，原因改写为停用原因；尝试记录保留连接失败的原文
        var deliveries = await _host.DeliveriesAsync();
        Assert.All(deliveries, x => Assert.Equal(WebhookDeliveryStatus.Failed, x.Status));
        Assert.Equal(
            WebhookSubscriptionDeactivator<WebhookTestDbContext>.DescribeDisabled(WebhookDisabledReason.ConsecutiveFailures),
            deliveries[^1].LastError);
        Assert.Equal("Connection refused.", Assert.Single(await _host.AttemptsAsync(deliveries[^1].Id)).Error);
    }

    /// <summary>A8：轮换过渡期内带新旧两把密钥（新的在前）；过渡期过后只带新密钥。</summary>
    [Fact]
    public async Task Both_secrets_sign_during_rotation_and_only_the_new_one_afterwards()
    {
        var (subscription, oldSecret) = await _host.AddSubscriptionAsync(null);
        var newSecret = await _host.InTenantAsync(null, async (provider, db) =>
        {
            var tracked = await db.Set<WebhookSubscription>().SingleAsync(x => x.Id == subscription.Id);
            var (secret, protectedSecret) = provider.GetRequiredService<WebhookSecretProtector>().CreateSecret();
            tracked.PreviousSecret = tracked.Secret;
            tracked.PreviousSecretExpiresAt = _host.Now.AddDays(1);
            tracked.Secret = protectedSecret;
            return secret;
        });

        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);
        _host.Clock.Advance(TimeSpan.FromDays(1));
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);

        var sent = _host.Sender.Sent.ToArray();
        Assert.Equal([newSecret, oldSecret], sent[0].Request.Secrets);
        Assert.Equal([newSecret], sent[1].Request.Secrets);
    }

    /// <summary>A16b：每次尝试记下当时实际发出的请求头；之后改订阅的请求头不改变旧尝试的快照。</summary>
    [Fact]
    public async Task Each_attempt_keeps_the_headers_it_was_sent_with()
    {
        var (subscription, _) = await _host.AddSubscriptionAsync(null);
        await SetHeadersAsync(subscription.Id, new() { ["X-Tenant"] = "old" });
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(503));
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);

        await SetHeadersAsync(subscription.Id, new() { ["X-Tenant"] = "new" });
        _host.Clock.Advance(TimeSpan.FromSeconds(6));
        await _host.RunDispatchAsync();

        var attempts = await _host.AttemptsAsync(Assert.Single(await _host.DeliveriesAsync()).Id);
        Assert.Equal(2, attempts.Count);
        Assert.Equal("old", attempts[0].RequestHeaders["X-Tenant"]);
        Assert.Equal("new", attempts[1].RequestHeaders["X-Tenant"]);
        Assert.Equal(503, attempts[0].StatusCode);
        Assert.Equal(12, attempts[0].DurationMs);
        Assert.All(attempts, x => Assert.Equal(WebhookAttemptTrigger.Automatic, x.Trigger));
    }

    /// <summary>
    /// A11（框架部分）：手动重发后队列项丢失，兜底扫描照样记为手动尝试、沿用事件 id；手动失败即回到失败，
    /// 带 Retry-After 的 503 也不重开自动退避，自动次数不变。
    /// </summary>
    [Fact]
    public async Task A_manual_retry_survives_a_lost_queue_item_and_is_not_rescheduled_after_failing()
    {
        await _host.AddSubscriptionAsync(null);
        _host.Sender.Default = ScriptedWebhookSender.Status(503, retryAfter: TimeSpan.FromMinutes(10));
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(400));
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);
        var failed = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Pending, failed.Status);

        // 把它推到终态：自动退避表用完之前不能手动重发
        await _host.InTenantAsync(null, async (_, db) =>
        {
            var tracked = await db.Set<WebhookDelivery>().SingleAsync();
            tracked.Status = WebhookDeliveryStatus.Failed;
            tracked.NextAttemptAt = null;
        });

        await _host.InTenantAsync(null, async (provider, db) =>
            provider.GetRequiredService<WebhookDeliveryQueue<WebhookTestDbContext>>()
                .ScheduleManualRetry(await db.Set<WebhookDelivery>().SingleAsync()));
        var scheduled = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Pending, scheduled.Status);
        Assert.Equal(WebhookAttemptTrigger.Manual, scheduled.NextTrigger);
        Assert.Equal(_host.Now, scheduled.NextAttemptAt);

        _host.Queue.Drop();
        await _host.RunDispatchAsync();

        var retried = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Failed, retried.Status);
        Assert.Null(retried.NextAttemptAt);
        Assert.Equal(WebhookAttemptTrigger.Automatic, retried.NextTrigger);
        Assert.Equal(2, retried.AttemptCount);
        Assert.Equal(1, retried.AutoAttemptCount);

        var attempts = await _host.AttemptsAsync(retried.Id);
        Assert.Equal([WebhookAttemptTrigger.Automatic, WebhookAttemptTrigger.Manual], attempts.Select(x => x.Trigger));
        var sent = _host.Sender.Sent.ToArray();
        Assert.Equal(sent[0].Request.EventId, sent[1].Request.EventId);

        _host.Clock.Advance(TimeSpan.FromHours(1));
        await _host.RunDispatchAsync();
        Assert.Equal(2, _host.Sender.Sent.Count);
    }

    /// <summary>重试中的投递不能手动重发；排期必须在工作单元里，随调用方一起提交。</summary>
    [Fact]
    public async Task Only_terminal_deliveries_can_be_scheduled_for_a_manual_retry()
    {
        await _host.AddSubscriptionAsync(null);
        await _host.PublishAsync(null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _host.InTenantAsync(null, async (provider, db) =>
            provider.GetRequiredService<WebhookDeliveryQueue<WebhookTestDbContext>>()
                .ScheduleManualRetry(await db.Set<WebhookDelivery>().SingleAsync())));

        await using var scope = _host.Services.CreateAsyncScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<WebhookDeliveryQueue<WebhookTestDbContext>>()
            .ScheduleManualRetry(new WebhookDelivery { Status = WebhookDeliveryStatus.Failed }));
    }

    private Task SetHeadersAsync(Guid subscriptionId, Dictionary<string, string> headers)
        => _host.InTenantAsync(null, async (_, db) =>
            (await db.Set<WebhookSubscription>().SingleAsync(x => x.Id == subscriptionId)).Headers = headers);
}
