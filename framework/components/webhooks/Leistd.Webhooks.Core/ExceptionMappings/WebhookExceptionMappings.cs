using System.Net;
using Leistd.ExceptionHandling.Options;
using Leistd.Webhooks.Errors;

namespace Leistd.Webhooks.ExceptionMappings;

// webhook 组件的非默认 HTTP 错误语义，由 AddWebhooks 登记；宿主用 MapCode 覆盖，与调用顺序无关。
internal static class WebhookExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(WebhookErrorCodes.SubscriptionNotFound, (int)HttpStatusCode.NotFound);
        options.MapDefaultCode(WebhookErrorCodes.DeliveryNotFound, (int)HttpStatusCode.NotFound);
        options.MapDefaultCode(WebhookErrorCodes.SubscriptionDisabled, (int)HttpStatusCode.Conflict);
        options.MapDefaultCode(WebhookErrorCodes.DeliveryNotRetryable, (int)HttpStatusCode.Conflict);
    }
}
