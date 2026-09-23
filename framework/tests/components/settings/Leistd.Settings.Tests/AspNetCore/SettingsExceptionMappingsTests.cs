using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.Settings.AspNetCore.ExceptionMappings;
using Leistd.Settings.Errors;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Leistd.Settings.Tests.AspNetCore;

public sealed class SettingsExceptionMappingsTests
{
    [Fact]
    public void Component_registers_its_non_default_http_statuses()
    {
        var options = new GlobalExceptionOptions();
        SettingsExceptionMappings.Configure(options);

        Assert.Equal(StatusCodes.Status403Forbidden, options.CodeStatusMappings[SettingErrorCodes.HostOnly]);
        Assert.Equal(StatusCodes.Status403Forbidden, options.CodeStatusMappings[SettingErrorCodes.IdentityCannotOperate]);
        Assert.Equal(StatusCodes.Status404NotFound, options.CodeStatusMappings[SettingErrorCodes.NotAvailable]);
        Assert.DoesNotContain(SettingErrorCodes.ValueOutOfRange, options.CodeStatusMappings.Keys);
    }
}
