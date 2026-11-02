using Leistd.TestBase.Doubles;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Options;
using Xunit;

namespace Leistd.Webhooks.Tests;

/// <summary>尝试结果按规格顺序表分类，命中即停：2xx → 410 → 手动 → 自动预算用完 → 自动退避。</summary>
/// <remarks>退避表固定为 10 秒、1 分钟两项，抖动源注入固定值，下次时刻可精确断言。</remarks>
public sealed class WebhookAttemptClassifierTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(200, WebhookAttemptTrigger.Automatic)]
    [InlineData(204, WebhookAttemptTrigger.Manual)]
    public void A_2xx_response_succeeds(int statusCode, WebhookAttemptTrigger trigger)
    {
        var outcome = Classify(Result(statusCode), trigger, autoAttemptCount: 1);

        Assert.Equal(new WebhookAttemptOutcome(WebhookDeliveryStatus.Succeeded, false, null), outcome);
    }

    /// <summary>410 排在手动与预算判断之前：无论哪种触发、还剩多少预算，都终结投递并停用订阅。</summary>
    [Theory]
    [InlineData(WebhookAttemptTrigger.Automatic, 1)]
    [InlineData(WebhookAttemptTrigger.Manual, 1)]
    [InlineData(WebhookAttemptTrigger.Automatic, 3)]
    public void A_410_response_fails_and_disables_the_subscription(WebhookAttemptTrigger trigger, int autoAttemptCount)
    {
        var outcome = Classify(Result(410), trigger, autoAttemptCount);

        Assert.Equal(new WebhookAttemptOutcome(WebhookDeliveryStatus.Failed, true, null), outcome);
    }

    /// <summary>手动尝试只发一次：可重试的响应（含带 Retry-After 的 429 / 503）、重定向与无响应都直接失败。</summary>
    [Theory]
    [InlineData(429, 120)]
    [InlineData(503, 120)]
    [InlineData(302, null)]
    [InlineData(500, null)]
    [InlineData(null, null)]
    public void A_failed_manual_attempt_fails_without_rescheduling(int? statusCode, int? retryAfterSeconds)
    {
        var outcome = Classify(Result(statusCode, retryAfterSeconds), WebhookAttemptTrigger.Manual, autoAttemptCount: 1);

        Assert.Equal(new WebhookAttemptOutcome(WebhookDeliveryStatus.Failed, false, null), outcome);
    }

    /// <summary>第 3 次自动尝试已超过两项退避表：同样的 429 / 503 + Retry-After 不再排期。</summary>
    [Theory]
    [InlineData(429, 120)]
    [InlineData(503, 120)]
    [InlineData(null, null)]
    public void A_failed_automatic_attempt_fails_once_the_schedule_is_used_up(int? statusCode, int? retryAfterSeconds)
    {
        var outcome = Classify(Result(statusCode, retryAfterSeconds), WebhookAttemptTrigger.Automatic, autoAttemptCount: 3);

        Assert.Equal(new WebhookAttemptOutcome(WebhookDeliveryStatus.Failed, false, null), outcome);
    }

    [Fact]
    public void An_empty_schedule_fails_on_the_first_automatic_failure()
    {
        var outcome = Classify(Result(500), WebhookAttemptTrigger.Automatic, autoAttemptCount: 1, schedule: []);

        Assert.Equal(WebhookDeliveryStatus.Failed, outcome.Status);
    }

    /// <summary>第 n 次自动尝试失败取退避表第 n 项；抖动源 0 / 0.5 / 1 对应 -10% / 0 / +10%。</summary>
    [Theory]
    [InlineData(1, 0.5, 10)]
    [InlineData(2, 0.5, 60)]
    [InlineData(1, 0.0, 9)]
    [InlineData(1, 1.0, 11)]
    public void A_failed_automatic_attempt_with_budget_is_rescheduled_by_the_schedule(
        int autoAttemptCount, double unitInterval, double expectedSeconds)
    {
        var outcome = Classify(Result(500), WebhookAttemptTrigger.Automatic, autoAttemptCount, unitInterval: unitInterval);

        Assert.Equal(new WebhookAttemptOutcome(WebhookDeliveryStatus.Pending, false, Now.AddSeconds(expectedSeconds)), outcome);
    }

    /// <summary>Retry-After 比退避间隔长时按 Retry-After 排期，短时仍按退避间隔。</summary>
    [Theory]
    [InlineData(429, 120, 120)]
    [InlineData(503, 120, 120)]
    [InlineData(503, 3, 10)]
    public void Retry_after_wins_when_it_is_longer_than_the_backoff(int statusCode, int retryAfterSeconds, double expectedSeconds)
    {
        var outcome = Classify(Result(statusCode, retryAfterSeconds), WebhookAttemptTrigger.Automatic, autoAttemptCount: 1);

        Assert.Equal(new WebhookAttemptOutcome(WebhookDeliveryStatus.Pending, false, Now.AddSeconds(expectedSeconds)), outcome);
    }

    [Fact]
    public void The_default_schedule_applies_when_none_is_configured()
    {
        var classifier = new WebhookAttemptClassifier(
            new MutableOptionsMonitor<WebhookOptions>(new WebhookOptions()), () => 0.5);

        var outcome = classifier.Classify(Result(500), WebhookAttemptTrigger.Automatic, WebhookOptions.DefaultRetrySchedule.Count, Now);

        Assert.Equal(Now + WebhookOptions.DefaultRetrySchedule[^1], outcome.NextAttemptAt);
    }

    private static WebhookAttemptOutcome Classify(
        WebhookSendResult result,
        WebhookAttemptTrigger trigger,
        int autoAttemptCount,
        TimeSpan[]? schedule = null,
        double unitInterval = 0.5)
    {
        var options = new WebhookOptions { RetrySchedule = schedule ?? [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1)] };
        return new WebhookAttemptClassifier(new MutableOptionsMonitor<WebhookOptions>(options), () => unitInterval)
            .Classify(result, trigger, autoAttemptCount, Now);
    }

    private static WebhookSendResult Result(int? statusCode, int? retryAfterSeconds = null) => new()
    {
        StatusCode = statusCode,
        Duration = TimeSpan.FromMilliseconds(5),
        RequestHeaders = new Dictionary<string, string>(),
        Error = statusCode is null ? "Connection refused." : null,
        RetryAfter = retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null
    };
}
