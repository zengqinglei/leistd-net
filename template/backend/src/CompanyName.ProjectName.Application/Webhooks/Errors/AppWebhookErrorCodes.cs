namespace CompanyName.ProjectName.Application.Webhooks.Errors;

/// <summary>项目 Webhook 留痕用的原因码；与 Webhook 组件的 Webhook 前缀分离。</summary>
/// <remarks>不作为接口错误抛出：只写进自动停用那条操作记录的失败原因，按读者语言渲染。</remarks>
public static class AppWebhookErrorCodes
{
    public const string EndpointGone = "AppWebhook:EndpointGone";
    public const string ConsecutiveFailures = "AppWebhook:ConsecutiveFailures";
}
