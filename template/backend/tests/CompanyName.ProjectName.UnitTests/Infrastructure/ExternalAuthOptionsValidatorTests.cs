#if (ExternalLogin)
using CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;

namespace CompanyName.ProjectName.UnitTests.Infrastructure;

public sealed class ExternalAuthOptionsValidatorTests
{
    private readonly ExternalAuthOptionsValidator _validator = new();

    [Fact]
    public void An_unconfigured_provider_is_valid_and_unavailable()
    {
        var options = new ExternalAuthOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.False(options.Github.IsAvailable);
        Assert.False(options.Google.IsAvailable);
    }

    [Fact]
    public void A_partially_configured_provider_reports_all_missing_fields()
    {
        var options = new ExternalAuthOptions();
        options.Github.ClientId = "client-id";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.StartsWith("ExternalAuth:Github:ClientSecret", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.StartsWith("ExternalAuth:Github:RedirectUri", StringComparison.Ordinal));
    }

    [Fact]
    public void A_complete_provider_is_valid_and_available()
    {
        var options = new ExternalAuthOptions();
        options.Github.ClientId = "client-id";
        options.Github.ClientSecret = "client-secret";
        options.Github.RedirectUri = "https://client.example.test/auth/callback";

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.True(options.Github.IsAvailable);
    }

    [Fact]
    public void Redirect_uri_must_be_an_absolute_http_uri()
    {
        var options = new ExternalAuthOptions();
        options.Github.ClientId = "client-id";
        options.Github.ClientSecret = "client-secret";
        options.Github.RedirectUri = "/auth/callback";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.StartsWith("ExternalAuth:Github:RedirectUri", StringComparison.Ordinal));
    }
}
#endif
