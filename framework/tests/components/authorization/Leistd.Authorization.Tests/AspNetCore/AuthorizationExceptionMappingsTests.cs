using Leistd.Authorization.AspNetCore.ExceptionMappings;
using Leistd.Authorization.Errors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Leistd.Authorization.Tests.AspNetCore;

public sealed class AuthorizationExceptionMappingsTests
{
    [Fact]
    public void Component_registers_only_non_default_statuses_and_host_can_override()
    {
        var options = new GlobalExceptionOptions();
        AuthorizationExceptionMappings.Configure(options);

        Assert.Equal(StatusCodes.Status404NotFound, options.CodeStatusMappings[PermissionErrorCodes.SubjectNotFound]);
        Assert.Equal(StatusCodes.Status409Conflict, options.CodeStatusMappings[PermissionErrorCodes.ConcurrencyConflict]);
        Assert.DoesNotContain(PermissionErrorCodes.UndefinedPermission, options.CodeStatusMappings.Keys);

        options.MapCode(PermissionErrorCodes.SubjectNotFound, StatusCodes.Status403Forbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, options.CodeStatusMappings[PermissionErrorCodes.SubjectNotFound]);

        var hostFirst = new GlobalExceptionOptions();
        hostFirst.MapCode(PermissionErrorCodes.SubjectNotFound, StatusCodes.Status403Forbidden);
        AuthorizationExceptionMappings.Configure(hostFirst);
        Assert.Equal(StatusCodes.Status403Forbidden,
            hostFirst.CodeStatusMappings[PermissionErrorCodes.SubjectNotFound]);
    }
}
