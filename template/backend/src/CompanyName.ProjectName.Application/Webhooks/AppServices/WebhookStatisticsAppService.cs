using CompanyName.ProjectName.Application.Settings.Timing;
using CompanyName.ProjectName.Application.Statistics;
using CompanyName.ProjectName.Application.Statistics.Dtos;
using CompanyName.ProjectName.Application.Webhooks.Dtos;
using Leistd.Ddd.Application.AppServices;
using Leistd.Timing;
using Leistd.Webhooks.Stores;

namespace CompanyName.ProjectName.Application.Webhooks.AppServices;

/// <summary>投递统计应用服务。</summary>
/// <remarks>
/// <para>日历区间、时区与上一区间按 <see cref="StatisticsPeriod"/> 换算，与平台统计同一约定；按桶计数经组件的
/// <see cref="IWebhookDeliveryReader"/> 在数据库里完成，组件不接收时区。</para>
/// <para>只读汇总，各项各查一次，不开工作单元：数字之间不要求同一快照。</para>
/// </remarks>
public class WebhookStatisticsAppService(
    IWebhookDeliveryReader deliveryReader,
    IUserTimeZoneProvider userTimeZoneProvider,
    IClock clock) : BaseAppService, IWebhookStatisticsAppService
{
    /// <inheritdoc />
    public async Task<WebhookStatisticsOutputDto> GetAsync(
        GetWebhookStatisticsInputDto input,
        CancellationToken cancellationToken = default)
    {
        var period = StatisticsPeriod.Resolve(input, userTimeZoneProvider, clock.Now);
        var subscriptionId = input.SubscriptionId;

        var buckets = period.Edges.Length < 2
            ? []
            : await deliveryReader.CountByBucketsAsync(period.Edges, subscriptionId, cancellationToken);
        var current = Sum(buckets);

        // 上一区间起点早于保留期下限时，那段的投递可能已被清理：不提供可比数据，而不是按 0 比较
        WebhookDeliveryCounts? previous = null;
        if (deliveryReader.GetRetainedSince() is not { } retainedSince || period.PreviousStart >= retainedSince)
        {
            previous = period.PreviousEnd > period.PreviousStart
                ? (await deliveryReader.CountByBucketsAsync(
                    [period.PreviousStart, period.PreviousEnd], subscriptionId, cancellationToken))[0]
                : new WebhookDeliveryCounts(0, 0, 0, 0);
        }

        var snapshot = await deliveryReader.GetSnapshotAsync(subscriptionId, cancellationToken);

        return new WebhookStatisticsOutputDto
        {
            Range = period.ToRange(),
            Buckets = period.Buckets,
            SuccessRate = new WebhookSuccessRateOutputDto
            {
                Value = SuccessRate(current),
                Previous = previous is null ? null : SuccessRate(previous),
                Series = [.. buckets.Select(SuccessRate)]
            },
            Deliveries = Metric(buckets, previous, counts => counts.Total),
            Succeeded = Metric(buckets, previous, counts => counts.Succeeded),
            Failed = Metric(buckets, previous, counts => counts.Failed),
            Retrying = new StatisticValueOutputDto { Value = snapshot.PendingDeliveries },
            DisabledSubscriptions = new StatisticValueOutputDto { Value = snapshot.DisabledSubscriptions }
        };
    }

    private static WebhookDeliveryCounts Sum(IReadOnlyList<WebhookDeliveryCounts> buckets) => new(
        buckets.Sum(counts => counts.Total),
        buckets.Sum(counts => counts.Succeeded),
        buckets.Sum(counts => counts.Failed),
        buckets.Sum(counts => counts.Pending));

    private static StatisticValueOutputDto Metric(
        IReadOnlyList<WebhookDeliveryCounts> buckets,
        WebhookDeliveryCounts? previous,
        Func<WebhookDeliveryCounts, int> pick) => new()
    {
        Value = buckets.Sum(pick),
        Previous = previous is null ? null : pick(previous),
        Series = [.. buckets.Select(pick)]
    };

    // 成功 ÷（成功 + 失败），不含重试中；分母为 0 时没有成功率
    private static double? SuccessRate(WebhookDeliveryCounts counts)
    {
        var terminal = counts.Succeeded + counts.Failed;
        return terminal == 0 ? null : (double)counts.Succeeded / terminal;
    }
}
