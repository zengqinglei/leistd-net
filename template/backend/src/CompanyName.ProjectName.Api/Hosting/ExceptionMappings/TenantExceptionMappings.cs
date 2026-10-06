#if (LocalIdentity && IncludeMultiTenancy)
using CompanyName.ProjectName.Application.Tenants.Errors;
using Leistd.ExceptionHandling.Options;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class TenantExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        ApiExceptionMappings.Map(options, StatusCodes.Status409Conflict,
            TenantErrorCodes.ActivateWithoutUsers,
            TenantErrorCodes.AdministratorNotFound,
            TenantErrorCodes.AlreadyImpersonating,
            TenantErrorCodes.NotImpersonating);
    }
}
#endif
