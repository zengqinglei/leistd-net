using System.ComponentModel.DataAnnotations;
#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
#endif
using CompanyName.ProjectName.Application.Webhooks.AppServices;
using CompanyName.ProjectName.Application.Webhooks.Dtos;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests.Webhooks;

/// <summary>投递统计：按创建时刻分桶、按当前状态计数，成功率不含重试中，上一区间超出保留期时没有可比数据。</summary>
/// <remarks>每个用例只统计自己建的订阅（<c>subscriptionId</c>），与同库里其他用例的投递互不干扰。</remarks>
public sealed class WebhookStatisticsTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// 七天区间：投递数含重试中；跨区间被手动重发成功的那条按当前状态计入它创建时的桶；
    /// 上一区间在保留期内，照常给出上期值；重试中与已停用是存量。
    /// </summary>
    [Fact]
    public async Task Counts_follow_creation_buckets_and_current_status()
    {
        var subscription = await InsertSubscriptionAsync(isEnabled: true);
        await InsertDeliveriesAsync(subscription,
            (Noon(-1), WebhookDeliveryStatus.Succeeded),
            (Noon(-1), WebhookDeliveryStatus.Succeeded),
            (Noon(-1), WebhookDeliveryStatus.Failed),
            (Noon(-1), WebhookDeliveryStatus.Pending),
            // 先失败、后被手动重发成功：只看当前状态
            (Noon(-3), WebhookDeliveryStatus.Succeeded),
            (Noon(-10), WebhookDeliveryStatus.Succeeded),
            (Noon(-10), WebhookDeliveryStatus.Failed));

        var statistics = await GetAsync(Today.AddDays(-6), Today, subscription);

        Assert.Equal(7, statistics.Buckets.Count);
        Assert.Equal(5, statistics.Deliveries.Value);
        Assert.Equal(2, statistics.Deliveries.Previous);
        Assert.Equal(3, statistics.Succeeded.Value);
        Assert.Equal(1, statistics.Failed.Value);
        Assert.Equal(1, statistics.Failed.Previous);
        Assert.Equal(0.75, statistics.SuccessRate.Value);
        Assert.Equal(0.5, statistics.SuccessRate.Previous);
        Assert.Equal(1, statistics.Retrying.Value);
        Assert.Equal(0, statistics.DisabledSubscriptions.Value);

        // 桶按下标对齐：前 6 天的第 3 个桶（今天 -3）只有一条成功，今天 -1 有两成功一失败；没有终态投递的桶成功率为空
        Assert.Equal([0, 0, 0, 1, 0, 4, 0], statistics.Deliveries.Series);
        Assert.Equal([null, null, null, 1.0, null, 2.0 / 3, null], statistics.SuccessRate.Series);
    }

    /// <summary>上一区间起点早于保留期下限（默认 62 天）：各项上期值为空，不按 0 比较；本期照常统计。</summary>
    [Fact]
    public async Task Previous_period_beyond_retention_has_no_comparable_data()
    {
        var subscription = await InsertSubscriptionAsync(isEnabled: false);
        await InsertDeliveriesAsync(subscription, (Noon(-60), WebhookDeliveryStatus.Failed));

        var statistics = await GetAsync(Today.AddDays(-60), Today.AddDays(-59), subscription);

        Assert.Equal(1, statistics.Deliveries.Value);
        Assert.Null(statistics.Deliveries.Previous);
        Assert.Null(statistics.Succeeded.Previous);
        Assert.Null(statistics.Failed.Previous);
        Assert.Equal(0.0, statistics.SuccessRate.Value);
        Assert.Null(statistics.SuccessRate.Previous);
        Assert.Equal(1, statistics.DisabledSubscriptions.Value);
    }

    /// <summary>区间校验与平台统计同一套：结束日晚于该时区的今天是字段错误。</summary>
    [Fact]
    public async Task An_end_date_after_today_is_a_field_error()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() => GetAsync(Today, Today.AddDays(2), null));

        Assert.Contains("EndDate", exception.ValidationResult.MemberNames);
    }
#if (LocalIdentity)

    /// <summary>HTTP 形状与前端约定一致：回显区间与桶，没有终态投递时成功率省略、上期值超出保留期时省略。</summary>
    [Fact]
    public async Task The_endpoint_echoes_the_range_and_omits_missing_rates()
    {
        var subscription = await InsertSubscriptionAsync(isEnabled: true);
        await InsertDeliveriesAsync(subscription, (Noon(-60), WebhookDeliveryStatus.Pending));
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        using var response = await admin.Client.GetAsync(
            $"/api/v1/webhooks/statistics?startDate={Today.AddDays(-60):yyyy-MM-dd}&endDate={Today.AddDays(-59):yyyy-MM-dd}" +
            $"&timeZone=UTC&subscriptionId={subscription}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("day", body.GetProperty("range").GetProperty("granularity").GetString());
        Assert.Equal(2, body.GetProperty("buckets").GetArrayLength());
        Assert.False(body.GetProperty("successRate").TryGetProperty("value", out _));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("successRate").GetProperty("series")[0].ValueKind);
        Assert.Equal(1, body.GetProperty("deliveries").GetProperty("value").GetInt32());
        Assert.False(body.GetProperty("deliveries").TryGetProperty("previous", out _));
        Assert.Equal(1, body.GetProperty("retrying").GetProperty("value").GetInt32());
    }
#endif

    private static DateTime Noon(int days) => Today.AddDays(days).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

    private async Task<WebhookStatisticsOutputDto> GetAsync(DateOnly startDate, DateOnly endDate, Guid? subscriptionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWebhookStatisticsAppService>().GetAsync(new GetWebhookStatisticsInputDto
        {
            StartDate = startDate,
            EndDate = endDate,
            TimeZone = "UTC",
            SubscriptionId = subscriptionId
        });
    }

    // 直接写表：要的是落在指定时刻、处于指定状态的投递，经发布只能落在"此刻"的待投递
    private async Task<Guid> InsertSubscriptionAsync(bool isEnabled)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var subscription = new WebhookSubscription
        {
            Name = "Statistics",
            Url = "https://receiver.invalid/hooks",
            EventTypes = ["*"],
            Secret = "not-used",
            IsEnabled = isEnabled
        };
        db.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    private async Task InsertDeliveriesAsync(Guid subscriptionId, params (DateTime CreationTime, WebhookDeliveryStatus Status)[] deliveries)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        foreach (var (creationTime, status) in deliveries)
        {
            var webhookEvent = new WebhookEvent { Type = "webhook.test", Payload = "{}", OccurredAt = creationTime };
            db.Add(webhookEvent);
            db.Add(new WebhookDelivery
            {
                EventId = webhookEvent.Id,
                SubscriptionId = subscriptionId,
                Status = status,
                NextAttemptAt = status == WebhookDeliveryStatus.Pending ? DateTime.UtcNow.AddDays(1) : null,
                CreationTime = creationTime
            });
        }

        await db.SaveChangesAsync();
    }
}
