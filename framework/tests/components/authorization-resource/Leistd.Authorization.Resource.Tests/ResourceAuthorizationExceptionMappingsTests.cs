using Leistd.Authorization.Resource.AspNetCore.ExceptionMappings;
using Leistd.Authorization.Resource.Errors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Leistd.Authorization.Resource.Tests;

public sealed class ResourceAuthorizationExceptionMappingsTests
{
    [Fact]
    public void Resource_defaults_are_transport_scoped_and_host_overridable()
    {
        var options = new GlobalExceptionOptions();
        ResourceAuthorizationExceptionMappings.Configure(options);

        Assert.Equal(StatusCodes.Status409Conflict,
            options.CodeStatusMappings[ResourceAuthorizationErrorCodes.ConcurrencyConflict]);
        Assert.DoesNotContain(ResourceAuthorizationErrorCodes.InvalidGrant, options.CodeStatusMappings.Keys);

        options.MapCode(ResourceAuthorizationErrorCodes.ConcurrencyConflict, StatusCodes.Status412PreconditionFailed);
        Assert.Equal(StatusCodes.Status412PreconditionFailed,
            options.CodeStatusMappings[ResourceAuthorizationErrorCodes.ConcurrencyConflict]);

        var hostFirst = new GlobalExceptionOptions();
        hostFirst.MapCode(ResourceAuthorizationErrorCodes.ConcurrencyConflict, StatusCodes.Status412PreconditionFailed);
        ResourceAuthorizationExceptionMappings.Configure(hostFirst);
        Assert.Equal(StatusCodes.Status412PreconditionFailed,
            hostFirst.CodeStatusMappings[ResourceAuthorizationErrorCodes.ConcurrencyConflict]);
    }
}
