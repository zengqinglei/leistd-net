using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Webhooks.AppServices;
using CompanyName.ProjectName.Application.Webhooks.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>Webhook 投递统计控制器：后台 Webhook 页的统计卡。订阅与投递日志的端点由组件映射（见 ComponentEndpoints）。</summary>
[Authorize]
[Route("api/v1/webhooks/statistics")]
public sealed class WebhookStatisticsController(IWebhookStatisticsAppService webhookStatisticsAppService) : BaseController
{
    /// <summary>按日历区间获取当前上下文的投递统计（需要 Webhook 查看权限）。</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstant.Webhooks.Default)]
    public async Task<WebhookStatisticsOutputDto> GetAsync(
        [FromQuery] GetWebhookStatisticsInputDto input,
        CancellationToken cancellationToken)
    {
        return await webhookStatisticsAppService.GetAsync(input, cancellationToken);
    }
}
