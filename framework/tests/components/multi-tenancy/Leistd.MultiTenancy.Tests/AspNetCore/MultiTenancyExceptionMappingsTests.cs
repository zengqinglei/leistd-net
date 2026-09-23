using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.MultiTenancy.AspNetCore.ExceptionMappings;
using Leistd.MultiTenancy.Errors;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Leistd.MultiTenancy.Tests.AspNetCore;

public sealed class MultiTenancyExceptionMappingsTests
{
    [Fact]
    public void Component_registers_its_non_default_http_statuses()
    {
        var options = new GlobalExceptionOptions();
        MultiTenancyExceptionMappings.Configure(options);

        Assert.Equal(StatusCodes.Status403Forbidden, options.CodeStatusMappings[MultiTenancyErrorCodes.NotActive]);
        Assert.Equal(StatusCodes.Status404NotFound, options.CodeStatusMappings[MultiTenancyErrorCodes.NotFound]);
        Assert.Equal(StatusCodes.Status409Conflict, options.CodeStatusMappings[MultiTenancyErrorCodes.ConcurrencyConflict]);
        Assert.Equal(StatusCodes.Status409Conflict, options.CodeStatusMappings[MultiTenancyErrorCodes.DuplicateName]);
        Assert.Equal(StatusCodes.Status409Conflict, options.CodeStatusMappings[MultiTenancyErrorCodes.ConnectionChangeRequiresInactiveTenant]);
        Assert.Equal(StatusCodes.Status409Conflict, options.CodeStatusMappings[MultiTenancyErrorCodes.ConnectionVersionConflict]);
    }
}
