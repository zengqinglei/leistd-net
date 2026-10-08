using System.Security.Claims;
using Leistd.Security.OpenIddict.Server;
using Leistd.Security.OpenIddict.Server.Pruning;
using Leistd.Security.OpenIddict.Validation;
using Leistd.Security.OpenIddict.Validation.SigningKeys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using global::OpenIddict.Abstractions;
using global::OpenIddict.Server;
using global::OpenIddict.Validation;
using global::OpenIddict.Validation.SystemNetHttp;
using Xunit;

namespace Leistd.Security.OpenIddict.Tests;

public sealed class ProtocolRegistrationTests
{
    [Fact]
    public async Task Configuration_and_delegate_control_the_actual_native_http_timeout()
    {
        var time = new FakeTimeProvider();
        using var host = new HostBuilder().ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keys:FetchTimeout"] = "00:00:03", ["Keys:MinimumInterval"] = "00:00:09"
        })).ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddOpenIddict().AddValidation(options =>
            {
                options.SetIssuer("https://identity.test/");
                options.UseSystemNetHttp();
            });
            services.AddSigningKeyRefresh(options => options.FetchTimeout = TimeSpan.FromSeconds(2), "Keys");
            services.AddSigningKeyRefresh(configSectionPath: "Keys");
        }).Build();
        await host.StartAsync();
        Assert.Same(time, host.Services.GetRequiredService<TimeProvider>());
        var settings = host.Services.GetRequiredService<IOptions<SigningKeyRefreshOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(3), settings.FetchTimeout);
        Assert.Equal(TimeSpan.FromSeconds(9), settings.MinimumInterval);
        var validation = host.Services.GetRequiredService<IOptions<OpenIddictValidationOptions>>().Value;
        Assert.IsType<SigningKeyRefreshThrottle>(validation.ConfigurationManager);
        Assert.Single(validation.Handlers, descriptor => descriptor == RefreshSigningKeysOnUnknownKeyIdentifier.Descriptor);
        Assert.NotNull(host.Services.GetRequiredService<RefreshSigningKeysOnUnknownKeyIdentifier>());
        using var client = new HttpClient();
        foreach (var action in host.Services.GetRequiredService<IOptions<OpenIddictValidationSystemNetHttpOptions>>().Value.HttpClientActions) action(client);
        Assert.Equal(settings.FetchTimeout, client.Timeout);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Keys:FetchTimeout", "00:00:00")]
    [InlineData("Keys:FetchTimeout", "30.00:00:00")]
    [InlineData("Keys:MinimumInterval", "00:00:00")]
    public async Task Invalid_refresh_settings_fail_at_start_with_the_actual_configuration_path(string key, string value)
    {
        using var host = new HostBuilder().ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }))
            .ConfigureServices(services => services.AddSigningKeyRefresh(configSectionPath: "Keys")).Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains(key, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task Pruning_rejects_retention_below_the_native_ten_minute_minimum(int minutes)
    {
        using var host = new HostBuilder().ConfigureServices(services => services.AddOpenIddictPruning(options => options.MinimumRetention = TimeSpan.FromMinutes(minutes), "Retention")).Build();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("Retention:MinimumRetention", error.Message);
    }

    [Theory]
    [InlineData("urn:ietf:params:oauth:grant-type:token-exchange", 60, 120, 60)]
    [InlineData("urn:ietf:params:oauth:grant-type:token-exchange", 120, 60, 60)]
    [InlineData("client_credentials", 60, 120, 120)]
    public async Task The_native_sign_in_handler_caps_only_exchange_tokens(string grantType, int subjectSeconds, int outputSeconds, int expectedSeconds)
    {
        var services = new ServiceCollection();
        services.AddTokenExchangeExpirationLimit();
        services.AddTokenExchangeExpirationLimit();
        using var provider = services.BuildServiceProvider();
        var options = new OpenIddictServerOptions();
        foreach (var configure in provider.GetServices<IConfigureOptions<OpenIddictServerOptions>>()) configure.Configure(options);
        var descriptor = Assert.Single(options.Handlers, descriptor => descriptor.Order == OpenIddictServerHandlers.PrepareIssuedTokenPrincipal.Descriptor.Order + 1);
        var now = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var context = new OpenIddictServerEvents.ProcessSignInContext(new OpenIddictServerTransaction())
        {
            Request = new OpenIddictRequest { GrantType = grantType },
            Principal = new ClaimsPrincipal(new ClaimsIdentity()),
            IssuedTokenPrincipal = new ClaimsPrincipal(new ClaimsIdentity())
        };
        context.Principal.SetExpirationDate(now.AddSeconds(subjectSeconds));
        context.IssuedTokenPrincipal.SetExpirationDate(now.AddSeconds(outputSeconds));
        using var scope = provider.CreateScope();
        var handler = (IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>)scope.ServiceProvider.GetRequiredService(descriptor.ServiceDescriptor.ServiceType);
        await handler.HandleAsync(context);
        Assert.Equal(now.AddSeconds(expectedSeconds), context.IssuedTokenPrincipal.GetExpirationDate());
    }
}
