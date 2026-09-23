using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.Notifications.AspNetCore.ExceptionMappings;
using Leistd.Notifications.Errors;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Leistd.Notifications.Tests.AspNetCore;

public sealed class NotificationExceptionMappingsTests
{
    [Fact]
    public void Component_registers_its_non_default_http_status()
    {
        var options = new GlobalExceptionOptions();
        NotificationExceptionMappings.Configure(options);

        Assert.Equal(StatusCodes.Status403Forbidden, options.CodeStatusMappings[NotificationErrorCodes.IdentityCannotOperate]);
    }
}
