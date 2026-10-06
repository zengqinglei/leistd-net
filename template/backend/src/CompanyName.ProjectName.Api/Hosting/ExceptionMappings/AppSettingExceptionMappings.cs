#if (Email)
using CompanyName.ProjectName.Application.Settings.Errors;
using Microsoft.AspNetCore.Http;
#endif
using Leistd.ExceptionHandling.Options;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class AppSettingExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
#if (Email)
        options.MapCode(AppSettingErrorCodes.TestEmailHostOnly, StatusCodes.Status403Forbidden);
        options.MapCode(AppSettingErrorCodes.TestEmailFailed, StatusCodes.Status503ServiceUnavailable);
        options.MapCode(AppSettingErrorCodes.EmailVerificationKeyMissing, StatusCodes.Status409Conflict);
#endif
    }
}
