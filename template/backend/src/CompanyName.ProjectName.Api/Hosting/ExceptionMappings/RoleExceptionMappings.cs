using CompanyName.ProjectName.Application.Roles.Errors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class RoleExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        options.MapCode(RoleErrorCodes.NotFound, StatusCodes.Status404NotFound);
        ApiExceptionMappings.Map(options, StatusCodes.Status409Conflict,
            RoleErrorCodes.NameAlreadyUsed,
            RoleErrorCodes.RoleStillAssigned,
            RoleErrorCodes.StaticRoleCannotBeDeleted);
    }
}
