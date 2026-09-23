#if (LocalIdentity)
using CompanyName.ProjectName.Application.Tenants.Errors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class TenantExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        options.MapCode(TenantErrorCodes.ImpersonationRequiresAuthentication, StatusCodes.Status401Unauthorized);
        ApiExceptionMappings.Map(options, StatusCodes.Status409Conflict,
            TenantErrorCodes.ActivateWithoutUsers,
            TenantErrorCodes.AdministratorNotFound,
            TenantErrorCodes.AlreadyImpersonating,
            TenantErrorCodes.NotImpersonating);
    }
}
#endif
