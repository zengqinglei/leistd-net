using System.Net;
using Leistd.Webhooks.Options;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.Delivery;

// 一次尝试结束后投递的去向。
// Status 为 Succeeded / Failed 时是终态，NextAttemptAt 为 null；为 Pending 时 NextAttemptAt 是下一次自动尝试时刻。
// DisablesSubscription 只在 410 时为 true：投递失败，且订阅以"端点已注销"停用。
internal readonly record struct WebhookAttemptOutcome(
    WebhookDeliveryStatus Status,
    bool DisablesSubscription,
    DateTime? NextAttemptAt);

// 规格"投递链路"第 5 步的顺序表，命中即停：2xx → 410 → 手动 → 自动预算用完 → 自动退避。
// 连续失败停用（FirstFailureAt 超过 AutoDisableAfter）依赖订阅状态，不在此判断。
internal sealed class WebhookAttemptClassifier
{
    internal const double JitterRatio = 0.1;

    private readonly IOptionsMonitor<WebhookOptions> _options;
    private readonly Func<double> _nextUnitInterval;

    public WebhookAttemptClassifier(IOptionsMonitor<WebhookOptions> options)
        : this(options, Random.Shared.NextDouble)
    {
    }

    // nextUnitInterval 返回 [0, 1) 的均匀随机数，映射为 [-10%, +10%) 的抖动；测试注入固定值。
    internal WebhookAttemptClassifier(IOptionsMonitor<WebhookOptions> options, Func<double> nextUnitInterval)
    {
        _options = options;
        _nextUnitInterval = nextUnitInterval;
    }

    // autoAttemptCount：本轮自动尝试数，含刚结束的这一次（首发失败时为 1）；手动尝试时取值不参与判断。
    // 第 n 次自动尝试失败后取退避表第 n 项（下标 n-1），n 超过表长即预算用完。now 为本次尝试结束时刻（UTC）。
    public WebhookAttemptOutcome Classify(
        WebhookSendResult result,
        WebhookAttemptTrigger trigger,
        int autoAttemptCount,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return new WebhookAttemptOutcome(WebhookDeliveryStatus.Succeeded, false, null);
        }

        if (result.StatusCode == (int)HttpStatusCode.Gone)
        {
            return new WebhookAttemptOutcome(WebhookDeliveryStatus.Failed, true, null);
        }

        if (trigger == WebhookAttemptTrigger.Manual)
        {
            return new WebhookAttemptOutcome(WebhookDeliveryStatus.Failed, false, null);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(autoAttemptCount, 1);
        var schedule = _options.CurrentValue.GetRetrySchedule();
        if (autoAttemptCount > schedule.Count)
        {
            return new WebhookAttemptOutcome(WebhookDeliveryStatus.Failed, false, null);
        }

        var backoff = schedule[autoAttemptCount - 1] * (1 + JitterRatio * (2 * _nextUnitInterval() - 1));
        var delay = result.RetryAfter is { } retryAfter && retryAfter > backoff ? retryAfter : backoff;
        return new WebhookAttemptOutcome(WebhookDeliveryStatus.Pending, false, now + delay);
    }
}
