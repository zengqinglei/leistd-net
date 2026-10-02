using CompanyName.ProjectName.Domain.Users.Errors;
using Leistd.ExceptionHandling.Options;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class UserExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        ApiExceptionMappings.Map(options, StatusCodes.Status403Forbidden,
            UserErrorCodes.ManageRolesRequired,
            UserErrorCodes.SuperAdminDeleteForbidden,
            UserErrorCodes.SuperAdminDisableForbidden,
            UserErrorCodes.SuperAdminDisableSelfForbidden,
            UserErrorCodes.SuperAdminOperationForbidden,
            UserErrorCodes.SuperAdminResetPasswordForbidden,
            UserErrorCodes.SuperAdminUpdateForbidden);
        options.MapCode(UserErrorCodes.NotFound, StatusCodes.Status404NotFound);
        ApiExceptionMappings.Map(options, StatusCodes.Status409Conflict,
            UserErrorCodes.EmailAlreadyUsed,
            UserErrorCodes.EmailTaken,
            UserErrorCodes.UsernameTaken);
    }
}
