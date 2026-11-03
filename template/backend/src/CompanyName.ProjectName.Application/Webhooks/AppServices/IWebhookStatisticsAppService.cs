using CompanyName.ProjectName.Application.Webhooks.Dtos;
using Leistd.Ddd.Application.Contracts.AppServices;

namespace CompanyName.ProjectName.Application.Webhooks.AppServices;

/// <summary>投递统计应用服务接口。</summary>
public interface IWebhookStatisticsAppService : IAppService
{
    /// <summary>按日历区间统计当前上下文（宿主或本租户）的投递，可按订阅筛选。</summary>
    /// <remarks>范围就是全局租户过滤器给出的当前层，不关闭过滤器、不遍历租户库。</remarks>
    Task<WebhookStatisticsOutputDto> GetAsync(
        GetWebhookStatisticsInputDto input,
        CancellationToken cancellationToken = default);
}
