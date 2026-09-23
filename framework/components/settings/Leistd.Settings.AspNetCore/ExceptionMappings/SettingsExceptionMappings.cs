using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.Settings.Errors;
using Microsoft.AspNetCore.Http;

namespace Leistd.Settings.AspNetCore.ExceptionMappings;

/// <summary>设置组件的非默认 HTTP 错误语义，由宿主显式组合。</summary>
public static class SettingsExceptionMappings
{
    /// <summary>登记设置组件的非默认状态映射。</summary>
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(SettingErrorCodes.HostOnly, StatusCodes.Status403Forbidden);
        options.MapDefaultCode(SettingErrorCodes.IdentityCannotOperate, StatusCodes.Status403Forbidden);
        options.MapDefaultCode(SettingErrorCodes.NotAvailable, StatusCodes.Status404NotFound);
    }
}
