using CompanyName.ProjectName.Application.Statistics.Dtos;

namespace CompanyName.ProjectName.Application.Webhooks.Dtos;

/// <summary>获取投递统计输入 DTO：日历区间与解释它的时区（规则见 <see cref="StatisticsRangeInputDto"/>），可按订阅筛选。</summary>
public record GetWebhookStatisticsInputDto : StatisticsRangeInputDto
{
    /// <summary>只统计该订阅；为空时统计当前上下文的全部订阅。</summary>
    public Guid? SubscriptionId { get; init; }
}
