#if (OpenIddictServer)
using System.ComponentModel.DataAnnotations;
using CompanyName.ProjectName.Application.OpenApplications.Validators;
using Microsoft.Extensions.Hosting;

namespace CompanyName.ProjectName.UnitTests.Application;

public sealed class OpenApplicationHomeUrlValidatorTests
{
    [Theory]
    [InlineData("Production", "https://example.com/", true)]
    [InlineData("Production", "http://localhost:4200/", false)]
    [InlineData("Development", "http://localhost:4200/", true)]
    [InlineData("Development", "http://127.0.0.1:4200/", true)]
    [InlineData("Development", "http://[::1]:4200/", true)]
    [InlineData("Development", "http://example.com/", false)]
    [InlineData("Production", "/platform", false)]
    [InlineData("Production", "javascript:alert(1)", false)]
    [InlineData("Production", "", false)]
    public void Home_URL_validation_matches_the_environment_and_reports_the_field(string environment, string url, bool valid)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        var validator = new OpenApplicationHomeUrlValidator(builder.Environment);
        if (valid) validator.Validate(url, true, "web");
        else Assert.Contains("HomeUrl", Assert.Throws<ValidationException>(() => validator.Validate(url, true, "web")).ValidationResult.MemberNames);
    }

    [Fact]
    public void Hidden_applications_can_omit_the_home_URL_and_only_browser_applications_can_be_shown()
    {
        var validator = new OpenApplicationHomeUrlValidator(Host.CreateApplicationBuilder().Environment);
        validator.Validate(null, false, "service");
        Assert.Contains("ShowInSwitcher", Assert.Throws<ValidationException>(() => validator.Validate("https://example.com", true, "service")).ValidationResult.MemberNames);
    }
}
#endif
