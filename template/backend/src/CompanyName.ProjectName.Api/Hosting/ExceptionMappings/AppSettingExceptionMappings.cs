using CompanyName.ProjectName.Application.Settings.Errors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class AppSettingExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        ApiExceptionMappings.Map(options, StatusCodes.Status403Forbidden,
            AppSettingErrorCodes.ManagePermissionRequired,
            AppSettingErrorCodes.TestEmailHostOnly);
        options.MapCode(AppSettingErrorCodes.TestEmailFailed, StatusCodes.Status503ServiceUnavailable);
        options.MapCode(AppSettingErrorCodes.EmailVerificationKeyMissing, StatusCodes.Status409Conflict);
    }
}
