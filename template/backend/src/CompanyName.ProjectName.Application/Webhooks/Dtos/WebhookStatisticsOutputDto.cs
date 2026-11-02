using CompanyName.ProjectName.Application.Statistics.Dtos;

namespace CompanyName.ProjectName.Application.Webhooks.Dtos;

/// <summary>投递统计输出 DTO：后台 Webhook 页顶部四张统计卡的数据，范围是当前上下文（宿主或本租户）。</summary>
/// <remarks>
/// <para>投递按创建时刻（即发布时刻）分桶、按<b>当前</b>状态计数：一个事件发给 N 个订阅计 N，手动重发改变终态后统计随之改变；
/// 已删除的订阅连同其投递退出统计。</para>
/// <para>上一区间起点早于保留期下限时，早于下限的投递可能已被清理，各项的 <c>Previous</c> 为 null（"无可比数据"），不按 0 计算。</para>
/// <para>各序列与 <see cref="Buckets"/> 按下标对齐，可按周相加（成功率除外）。</para>
/// </remarks>
public record WebhookStatisticsOutputDto
{
    /// <summary>本次统计实际采用的区间、时区与粒度。</summary>
    public required StatisticsRangeOutputDto Range { get; init; }

    /// <summary>各统计桶起点的 UTC 时刻，升序；最后一个桶止于 <see cref="StatisticsRangeOutputDto.End"/>，零长度区间为空。</summary>
    public required IReadOnlyList<DateTime> Buckets { get; init; }

    /// <summary>成功率：成功 ÷（成功 + 失败），不含重试中。</summary>
    public required WebhookSuccessRateOutputDto SuccessRate { get; init; }

    /// <summary>区间内创建的投递数。</summary>
    public required StatisticValueOutputDto Deliveries { get; init; }

    /// <summary>区间内创建、当前为成功的投递数。</summary>
    public required StatisticValueOutputDto Succeeded { get; init; }

    /// <summary>区间内创建、当前为失败的投递数。</summary>
    public required StatisticValueOutputDto Failed { get; init; }

    /// <summary>当前仍在重试中的投递数：存量，不随区间变化，只有本期值。</summary>
    public required StatisticValueOutputDto Retrying { get; init; }

    /// <summary>当前已停用的订阅数：存量，不随区间变化，只有本期值；按订阅统计时为 0 或 1。</summary>
    public required StatisticValueOutputDto DisabledSubscriptions { get; init; }
}

/// <summary>成功率（0–1）的本期值、上期值与按桶序列；分母为 0 的区间或桶为 null。</summary>
public record WebhookSuccessRateOutputDto
{
    /// <summary>本区间的成功率；区间内没有终态投递时为 null。</summary>
    public double? Value { get; init; }

    /// <summary>上一区间的成功率；上一区间没有终态投递或早于保留期下限时为 null。</summary>
    public double? Previous { get; init; }

    /// <summary>按桶的成功率，与 <see cref="WebhookStatisticsOutputDto.Buckets"/> 等长；没有终态投递的桶为 null。</summary>
    public required IReadOnlyList<double?> Series { get; init; }
}
