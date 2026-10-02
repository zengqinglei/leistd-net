using System.Net;
using Leistd.ExceptionHandling.Options;
using Leistd.Notifications.Errors;

namespace Leistd.Notifications.ExceptionMappings;

// 通知组件的非默认 HTTP 错误语义。
// AddNotifications 已自动登记，宿主不需要调用。默认值同时列在组件文档里；
// 要改其中任何一条，在自己的 GlobalExceptionOptions 里用 MapCode 覆盖，与调用顺序无关。
internal static class NotificationExceptionMappings
{
    // 登记通知组件的非默认状态映射。
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(NotificationErrorCodes.IdentityCannotOperate, (int)HttpStatusCode.Forbidden);
    }
}
