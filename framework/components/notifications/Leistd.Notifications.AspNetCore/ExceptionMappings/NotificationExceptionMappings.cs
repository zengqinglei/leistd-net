using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.Notifications.Errors;
using Microsoft.AspNetCore.Http;

namespace Leistd.Notifications.AspNetCore.ExceptionMappings;

/// <summary>通知组件的非默认 HTTP 错误语义，由宿主显式组合。</summary>
public static class NotificationExceptionMappings
{
    /// <summary>登记通知组件的非默认状态映射。</summary>
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(NotificationErrorCodes.IdentityCannotOperate, StatusCodes.Status403Forbidden);
    }
}
