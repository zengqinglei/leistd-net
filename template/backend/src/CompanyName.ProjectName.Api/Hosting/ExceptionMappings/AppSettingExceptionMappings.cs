using CompanyName.ProjectName.Application.Settings.Errors;
using Leistd.ExceptionHandling.Options;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class AppSettingExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        // 设置页的"测试"失败（发信、导出）：请求本身有效，是这组配置用不了，统一 422。
        // 不用 5xx：前端把写请求的 5xx 当作"可能已生效"，只留错误码，界面就给不出原因
#if (Email)
        options.MapCode(AppSettingErrorCodes.TestEmailFailed, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.TestEmailUnreachable, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.TestEmailAuthRejected, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.TestEmailRejected, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.EmailVerificationKeyMissing, StatusCodes.Status409Conflict);
#endif
        options.MapCode(AppSettingErrorCodes.TelemetryExportNotConfigured, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.TelemetryExportUnavailable, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.TelemetryExportAuthRejected, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.TelemetryExportUnreachable, StatusCodes.Status422UnprocessableEntity);
        options.MapCode(AppSettingErrorCodes.TelemetryExportUnexpectedStatus, StatusCodes.Status422UnprocessableEntity);
    }
}
