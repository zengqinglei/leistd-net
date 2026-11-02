using Leistd.ExceptionHandling;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.Errors;
using Leistd.Webhooks.Tests.TestDoubles;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>投递日志的管理用例：A11 手动重发只对终态开放、停用订阅上不能重发或发测试事件、测试事件只投给该订阅、详情含载荷与倒序尝试。</summary>
/// <remarks>重试中的投递若能再手动重发，会与已有排期各发一次；停用订阅上放行则绕过了"停用期间不发请求"。</remarks>
public sealed class WebhookDeliveryManagementTests : IAsyncLifetime
{
    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>A11：重试中（待投递）的投递手动重发返回 <c>DeliveryNotRetryable</c>，排期不变、不入队。</summary>
    [Fact]
    public async Task A_pending_delivery_cannot_be_retried_manually()
    {
        await CreateAsync();
        await _host.PublishAsync(null);
        _host.Queue.Drop();
        var pending = Assert.Single(await _host.DeliveriesAsync());

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => _host.ManageAsync(null, service => service.RetryDeliveryAsync(pending.Id)));

        Assert.Equal(WebhookErrorCodes.DeliveryNotRetryable, error.Code);
        var stored = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal((pending.NextAttemptAt, WebhookAttemptTrigger.Automatic), (stored.NextAttemptAt, stored.NextTrigger));
        Assert.Equal(0, _host.Queue.Count);
    }

    /// <summary>A11：终态投递重发后发出一次手动尝试，事件标识不变，自动次数不变。</summary>
    [Theory]
    [InlineData(200)]
    [InlineData(410)]
    public async Task A_terminal_delivery_is_retried_once_with_the_same_event_id(int firstStatus)
    {
        var subscription = await CreateAsync();
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(firstStatus));
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);
        if (firstStatus == 410)
        {
            await _host.ManageAsync(null, service => service.EnableSubscriptionAsync(subscription));
        }

        var terminal = Assert.Single(await _host.DeliveriesAsync());
        var output = await _host.ManageAsync(null, service => service.RetryDeliveryAsync(terminal.Id));

        Assert.Equal((WebhookDeliveryStatus.Pending, WebhookAttemptTrigger.Manual), (output.Status, output.NextTrigger));
        await _host.Queue.RunAllAsync(_host.Services);
        var retried = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(WebhookDeliveryStatus.Succeeded, retried.Status);
        Assert.Equal((2, 1), (retried.AttemptCount, retried.AutoAttemptCount));
        Assert.Equal(WebhookAttemptTrigger.Manual, (await _host.AttemptsAsync(retried.Id)).Last().Trigger);
        Assert.All(_host.Sender.Sent, sent => Assert.Equal(terminal.EventId, sent.Request.EventId));
    }

    /// <summary>订阅已停用时，测试事件与手动重发都返回 <c>SubscriptionDisabled</c>，不写投递、不入队。</summary>
    [Fact]
    public async Task A_disabled_subscription_rejects_test_events_and_manual_retries()
    {
        var subscription = await CreateAsync();
        await _host.PublishAsync(null);
        await _host.ManageAsync(null, service => service.DisableSubscriptionAsync(subscription));
        _host.Queue.Drop();
        var terminated = Assert.Single(await _host.DeliveriesAsync());

        var test = await Assert.ThrowsAsync<BusinessException>(() => _host.ManageAsync(null, service => service.SendTestEventAsync(subscription)));
        var retry = await Assert.ThrowsAsync<BusinessException>(() => _host.ManageAsync(null, service => service.RetryDeliveryAsync(terminated.Id)));

        Assert.Equal(WebhookErrorCodes.SubscriptionDisabled, test.Code);
        Assert.Equal(WebhookErrorCodes.SubscriptionDisabled, retry.Code);
        Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(0, _host.Queue.Count);
    }

    /// <summary>测试事件只投给指定订阅，即使它没有订阅测试类型；同租户里订阅了全部事件的订阅也收不到。</summary>
    [Fact]
    public async Task A_test_event_is_delivered_only_to_the_chosen_subscription()
    {
        var subscription = await CreateAsync();
        await CreateAsync(WebhookDefinition.AllEventTypes);

        var output = await _host.ManageAsync(null, service => service.SendTestEventAsync(subscription));
        await _host.Queue.RunAllAsync(_host.Services);

        Assert.Equal((WebhookDefinition.TestEventType, subscription), (output.EventType, output.SubscriptionId));
        var sent = Assert.Single(_host.Sender.Sent);
        Assert.Equal((WebhookDefinition.TestEventType, subscription), (sent.Request.EventType, sent.Request.SubscriptionId));
    }

    /// <summary>详情含发布时冻结的载荷与全部尝试，尝试按时刻倒序；列表按状态与订阅筛选。</summary>
    [Fact]
    public async Task The_detail_has_the_payload_and_attempts_newest_first_and_the_list_filters()
    {
        var subscription = await CreateAsync();
        await CreateAsync(WebhookTestHost.OrderShipped);
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(500), ScriptedWebhookSender.Status(200));
        await _host.PublishAsync(null, data: new { orderId = 7 });
        await _host.Queue.RunAllAsync(_host.Services);
        _host.Clock.Advance(TimeSpan.FromSeconds(6));
        await _host.RunDispatchAsync();
        var delivery = Assert.Single(await _host.DeliveriesAsync());

        var detail = await _host.ManageAsync(null, service => service.GetDeliveryAsync(delivery.Id));
        var failed = await _host.ManageAsync(null, service => service.GetDeliveriesAsync(new GetWebhookDeliveriesInputDto
        {
            SubscriptionId = subscription,
            Status = WebhookDeliveryStatus.Failed
        }));
        var succeeded = await _host.ManageAsync(null, service => service.GetDeliveriesAsync(new GetWebhookDeliveriesInputDto
        {
            SubscriptionId = subscription,
            Status = WebhookDeliveryStatus.Succeeded,
            Keyword = delivery.EventId.ToString()
        }));

        Assert.Contains("\"orderId\":7", detail.Payload);
        Assert.Equal([200, 500], detail.Attempts.Select(x => x.StatusCode ?? 0));
        Assert.True(detail.Attempts[0].AttemptedAt > detail.Attempts[1].AttemptedAt);
        Assert.Equal(0, failed.TotalCount);
        Assert.Equal(delivery.Id, Assert.Single(succeeded.Items).Id);
    }

    private async Task<Guid> CreateAsync(params string[] eventTypes)
        => (await _host.ManageAsync(null, service => service.CreateSubscriptionAsync(new CreateWebhookSubscriptionInputDto
        {
            Name = "receiver",
            Url = "https://receiver.example/hook",
            EventTypes = eventTypes.Length == 0 ? [WebhookTestHost.OrderPaid] : eventTypes
        }))).Subscription.Id;
}
