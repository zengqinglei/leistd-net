using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Leistd.ExceptionHandling;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Errors;
using Leistd.Webhooks.Signing;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>
/// 订阅的管理用例：A9 明文密钥只在创建与轮换时返回一次；<c>"*"</c> 只能单独出现并匹配任何类型；入参校验；
/// 近 7 天成功率口径；删除连带投递与尝试；A12 租户 A 看不到 B。
/// </summary>
public sealed class WebhookSubscriptionManagementTests : IAsyncLifetime
{
    private WebhookTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WebhookTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>A9：创建与轮换的结果带明文，详情与列表只有前缀；轮换后旧密钥在过渡期内继续签名。</summary>
    [Fact]
    public async Task The_plaintext_secret_is_returned_only_on_creation_and_rotation()
    {
        var created = await CreateAsync(_host, null, "a");
        var id = created.Subscription.Id;

        var detail = await _host.ManageAsync(null, service => service.GetSubscriptionAsync(id));
        var list = await _host.ManageAsync(null, service => service.GetSubscriptionsAsync(new GetWebhookSubscriptionsInputDto()));
        var rotated = await _host.ManageAsync(null, service => service.RotateSecretAsync(id));
        var afterRotation = await _host.ManageAsync(null, service => service.GetSubscriptionAsync(id));

        Assert.StartsWith("whsec_", created.Secret);
        Assert.Equal(created.Secret[.."whsec_xxxx".Length], detail.SecretPrefix);
        Assert.DoesNotContain(created.Secret, JsonSerializer.Serialize(detail));
        Assert.DoesNotContain(created.Secret, JsonSerializer.Serialize(list));
        Assert.DoesNotContain(created.Secret, created.ToString());

        Assert.NotEqual(created.Secret, rotated.Secret);
        Assert.Equal(rotated.Secret[.."whsec_xxxx".Length], afterRotation.SecretPrefix);
        Assert.DoesNotContain(rotated.Secret, JsonSerializer.Serialize(afterRotation));
        Assert.Equal(_host.Now.AddDays(1), afterRotation.PreviousSecretExpiresAt);
        var stored = await _host.SubscriptionAsync(id);
        Assert.Equal(
            [rotated.Secret, created.Secret],
            _host.Services.GetRequiredService<WebhookSecretProtector>().GetSigningSecrets(
                stored.Id, stored.Secret, stored.PreviousSecret, stored.PreviousSecretExpiresAt, _host.Now));
    }

    /// <summary>不合法的入参在任何写入之前以字段校验失败拒绝。</summary>
    [Theory]
    [InlineData("https://r.example/hook", new[] { "*", WebhookTestHost.OrderPaid }, null, "EventTypes")]
    [InlineData("https://r.example/hook", new[] { "order.unknown" }, null, "EventTypes")]
    [InlineData("https://r.example/hook", new[] { WebhookDefinition.TestEventType }, null, "EventTypes")]
    [InlineData("ftp://r.example/hook", new[] { WebhookTestHost.OrderPaid }, null, "Url")]
    [InlineData("/relative/hook", new[] { WebhookTestHost.OrderPaid }, null, "Url")]
    [InlineData("https://r.example/hook", new[] { WebhookTestHost.OrderPaid }, "webhook-signature", "Headers")]
    [InlineData("https://r.example/hook", new[] { WebhookTestHost.OrderPaid }, "Content-Type", "Headers")]
    [InlineData("https://r.example/hook", new[] { WebhookTestHost.OrderPaid }, "bad header", "Headers")]
    public async Task Invalid_input_is_rejected_before_anything_is_written(string url, string[] eventTypes, string? header, string field)
    {
        var input = new CreateWebhookSubscriptionInputDto
        {
            Name = "a",
            Url = url,
            EventTypes = eventTypes,
            Headers = header is null ? null : new Dictionary<string, string> { [header] = "v" }
        };

        var error = await Assert.ThrowsAsync<ValidationException>(() => _host.ManageAsync(null, service => service.CreateSubscriptionAsync(input)));

        Assert.Equal([field], error.ValidationResult.MemberNames);
        Assert.Empty(await _host.ReadAsync(db => db.Set<WebhookSubscription>().IgnoreQueryFilters().ToListAsync()));
    }

    /// <summary>更新同样校验，校验先于查找；找不到的订阅返回 <c>SubscriptionNotFound</c>。</summary>
    [Fact]
    public async Task Updating_validates_the_input_and_reports_a_missing_subscription()
    {
        var invalid = new UpdateWebhookSubscriptionInputDto { Name = "a", Url = "https://r.example", EventTypes = ["*", WebhookTestHost.OrderPaid] };
        var valid = invalid with { EventTypes = ["*"] };

        await Assert.ThrowsAsync<ValidationException>(() => _host.ManageAsync(null, service => service.UpdateSubscriptionAsync(Guid.NewGuid(), invalid)));
        var missing = await Assert.ThrowsAsync<BusinessException>(
            () => _host.ManageAsync(null, service => service.UpdateSubscriptionAsync(Guid.NewGuid(), valid)));

        Assert.Equal(WebhookErrorCodes.SubscriptionNotFound, missing.Code);
    }

    /// <summary>按事件类型筛选时，订阅了 <c>"*"</c> 的也算匹配；关键字在名称与地址上大小写不敏感；按启用状态筛选。</summary>
    [Fact]
    public async Task The_list_filters_by_event_type_keyword_and_state()
    {
        var paid = await CreateAsync(_host, null, "Paid hook", WebhookTestHost.OrderPaid);
        var all = await CreateAsync(_host, null, "All events", WebhookDefinition.AllEventTypes);
        var shipped = await CreateAsync(_host, null, "Shipping", WebhookTestHost.OrderShipped);
        await _host.ManageAsync(null, service => service.DisableSubscriptionAsync(shipped.Subscription.Id));

        var byType = await List(new GetWebhookSubscriptionsInputDto { EventType = WebhookTestHost.OrderPaid });
        var byKeyword = await List(new GetWebhookSubscriptionsInputDto { Keyword = "RECEIVER.Example" });
        var disabled = await List(new GetWebhookSubscriptionsInputDto { IsEnabled = false });

        Assert.Equal([all.Subscription.Id, paid.Subscription.Id], byType);
        Assert.Equal([shipped.Subscription.Id, all.Subscription.Id, paid.Subscription.Id], byKeyword);
        Assert.Equal([shipped.Subscription.Id], disabled);

        async Task<Guid[]> List(GetWebhookSubscriptionsInputDto input)
            => [.. (await _host.ManageAsync(null, service => service.GetSubscriptionsAsync(input))).Items.Select(x => x.Id)];
    }

    /// <summary>
    /// 近 7 天成功率 = 最近 7×24 小时内创建的投递里 成功 ÷（成功 + 失败），不含重试中，窗口外的不计；没有终态投递时为空。
    /// 最近一次投递取创建时间最新的那条。
    /// </summary>
    [Fact]
    public async Task The_success_rate_counts_terminal_deliveries_created_in_the_last_seven_days()
    {
        var measured = (await CreateAsync(_host, null, "measured")).Subscription.Id;
        var onlyPending = (await CreateAsync(_host, null, "pending")).Subscription.Id;
        var now = _host.Now;
        await SeedAsync(measured,
            (now.AddDays(-8), WebhookDeliveryStatus.Failed),
            (now.AddDays(-6), WebhookDeliveryStatus.Succeeded),
            (now.AddDays(-3), WebhookDeliveryStatus.Succeeded),
            (now.AddDays(-2), WebhookDeliveryStatus.Failed),
            (now.AddHours(-1), WebhookDeliveryStatus.Succeeded),
            (now.AddMinutes(-1), WebhookDeliveryStatus.Pending));
        await SeedAsync(onlyPending, (now.AddMinutes(-5), WebhookDeliveryStatus.Pending));

        var page = await _host.ManageAsync(null, service => service.GetSubscriptionsAsync(new GetWebhookSubscriptionsInputDto()));
        var empty = (await CreateAsync(_host, null, "empty")).Subscription;

        var row = page.Items.Single(x => x.Id == measured);
        Assert.Equal(0.75, row.SuccessRateLast7Days);
        Assert.Equal((WebhookDeliveryStatus.Pending, now.AddMinutes(-1)), (row.LastDelivery!.Status, row.LastDelivery.CreationTime));
        Assert.Null(page.Items.Single(x => x.Id == onlyPending).SuccessRateLast7Days);
        Assert.Null(empty.SuccessRateLast7Days);
        Assert.Null(empty.LastDelivery);
    }

    /// <summary>删除订阅时一并删除它的投递与尝试，其他订阅的不受影响；之后再取返回 <c>SubscriptionNotFound</c>。</summary>
    [Fact]
    public async Task Deleting_a_subscription_removes_its_deliveries_and_attempts()
    {
        var doomed = (await CreateAsync(_host, null, "doomed")).Subscription.Id;
        var kept = (await CreateAsync(_host, null, "kept")).Subscription.Id;
        await _host.PublishAsync(null);
        await _host.Queue.RunAllAsync(_host.Services);
        Assert.Equal(2, await _host.ReadAsync(db => db.Set<WebhookAttempt>().CountAsync()));

        await _host.ManageAsync(null, service => service.DeleteSubscriptionAsync(doomed));

        var remaining = Assert.Single(await _host.DeliveriesAsync());
        Assert.Equal(kept, remaining.SubscriptionId);
        Assert.Equal(remaining.Id, Assert.Single(await _host.ReadAsync(db => db.Set<WebhookAttempt>().ToListAsync())).DeliveryId);
        var missing = await Assert.ThrowsAsync<BusinessException>(() => _host.ManageAsync(null, service => service.GetSubscriptionAsync(doomed)));
        Assert.Equal(WebhookErrorCodes.SubscriptionNotFound, missing.Code);
    }

    /// <summary>A12：租户 A 的管理员看不到 B 的订阅与投递，按标识取、改、删 B 的订阅一律当作不存在。</summary>
    [Fact]
    public async Task A_tenant_cannot_see_or_change_another_tenants_subscriptions()
    {
        var ofB = (await CreateAsync(_host, WebhookTestHost.TenantB, "b")).Subscription.Id;
        await _host.PublishAsync(WebhookTestHost.TenantB);
        var deliveryOfB = Assert.Single(await _host.DeliveriesAsync()).Id;

        var subscriptions = await _host.ManageAsync(WebhookTestHost.TenantA, service => service.GetSubscriptionsAsync(new GetWebhookSubscriptionsInputDto()));
        var deliveries = await _host.ManageAsync(WebhookTestHost.TenantA, service => service.GetDeliveriesAsync(new GetWebhookDeliveriesInputDto()));
        var get = await Assert.ThrowsAsync<BusinessException>(() => _host.ManageAsync(WebhookTestHost.TenantA, service => service.GetSubscriptionAsync(ofB)));
        var delete = await Assert.ThrowsAsync<BusinessException>(() => _host.ManageAsync(WebhookTestHost.TenantA, service => service.DeleteSubscriptionAsync(ofB)));
        var detail = await Assert.ThrowsAsync<BusinessException>(() => _host.ManageAsync(WebhookTestHost.TenantA, service => service.GetDeliveryAsync(deliveryOfB)));

        Assert.Equal(0, subscriptions.TotalCount);
        Assert.Equal(0, deliveries.TotalCount);
        Assert.Equal(WebhookErrorCodes.SubscriptionNotFound, get.Code);
        Assert.Equal(WebhookErrorCodes.SubscriptionNotFound, delete.Code);
        Assert.Equal(WebhookErrorCodes.DeliveryNotFound, detail.Code);
        Assert.Equal(WebhookTestHost.TenantB, (await _host.SubscriptionAsync(ofB)).TenantId);
        Assert.Equal(1, (await _host.ManageAsync(WebhookTestHost.TenantB, service => service.GetDeliveriesAsync(new GetWebhookDeliveriesInputDto()))).TotalCount);
    }

    /// <summary>事件类型列表只含业务登记的类型，不含内置测试事件。</summary>
    [Fact]
    public async Task The_event_types_exclude_the_built_in_test_event()
    {
        var types = await _host.ManageAsync(null, service => service.GetEventTypesAsync());

        Assert.Equal([WebhookTestHost.OrderPaid, WebhookTestHost.OrderShipped], types.Select(x => x.Name));
    }

    // 每建一个推进 1 秒：列表按创建时间倒序，同一时刻建的订阅顺序不确定
    private static async Task<WebhookSubscriptionSecretOutputDto> CreateAsync(WebhookTestHost host, Guid? tenantId, string name, params string[] eventTypes)
    {
        var created = await host.ManageAsync(tenantId, service => service.CreateSubscriptionAsync(new CreateWebhookSubscriptionInputDto
        {
            Name = name,
            Url = $"https://receiver.example/{name.Replace(' ', '-')}",
            EventTypes = eventTypes.Length == 0 ? [WebhookTestHost.OrderPaid] : eventTypes
        }));
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        return created;
    }

    private Task SeedAsync(Guid subscriptionId, params (DateTime CreationTime, WebhookDeliveryStatus Status)[] deliveries)
        => _host.InTenantAsync(null, (_, db) =>
        {
            foreach (var (creationTime, status) in deliveries)
            {
                var webhookEvent = new WebhookEvent { Type = WebhookTestHost.OrderPaid, Payload = "{}", OccurredAt = creationTime };
                db.Add(webhookEvent);
                db.Add(new WebhookDelivery
                {
                    EventId = webhookEvent.Id,
                    SubscriptionId = subscriptionId,
                    Status = status,
                    NextAttemptAt = status == WebhookDeliveryStatus.Pending ? _host.Now.AddHours(1) : null,
                    CreationTime = creationTime
                });
            }

            return Task.CompletedTask;
        });
}
