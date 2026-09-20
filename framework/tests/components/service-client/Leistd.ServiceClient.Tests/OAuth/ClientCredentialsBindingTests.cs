using Leistd.ServiceClient.OAuth;
using Leistd.ServiceClient.OAuth.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.ServiceClient.Tests.OAuth;

/// <summary>
/// 认证配置分层绑定：全局 <c>Leistd:ServiceAuth</c>（本服务调用身份，一次）
/// + 客户端节 <c>Leistd:ServiceClients:&lt;名&gt;:Scope</c>（目标服务级，可省）。
/// </summary>
public class ClientCredentialsBindingTests
{
    private static ClientCredentialsOptions Resolve(string clientName, Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(clientName).AddClientCredentials(configuration);
        return services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<ClientCredentialsOptions>>()
            .Get(clientName);
    }

    /// <summary>凭据漏配在启动期就失败，而不是拖到第一次跨服务调用。</summary>
    [Theory]
    [InlineData("Leistd:ServiceAuth:ClientId")]
    [InlineData("Leistd:ServiceAuth:ClientSecret")]
    [InlineData("Leistd:ServiceAuth:Authority")]
    public void Missing_credentials_fail_the_host_at_startup(string missingKey)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:Authority"] = "http://identity-service",
            ["Leistd:ServiceAuth:ClientId"] = "b-service",
            ["Leistd:ServiceAuth:ClientSecret"] = "s3cret",
        };
        configValues.Remove(missingKey);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("OrderService").AddClientCredentials(configuration);
        using var provider = services.BuildServiceProvider();

        var startupValidator = provider.GetServices<IStartupValidator>().Single();
        var failure = Assert.Throws<OptionsValidationException>(startupValidator.Validate);
        Assert.Contains(failure.Failures, message => message.StartsWith(missingKey, StringComparison.Ordinal)
            || message.StartsWith("Leistd:ServiceAuth:Authority or TokenEndpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void Global_identity_is_shared_and_scopes_differ_per_client()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:Authority"] = "http://identity-service",
            ["Leistd:ServiceAuth:ClientId"] = "b-service",
            ["Leistd:ServiceAuth:ClientSecret"] = "s3cret",
            ["Leistd:ServiceClients:OrderService:BaseAddress"] = "http://order-service",
            ["Leistd:ServiceClients:OrderService:Scope"] = "order-api",
        };

        var options = Resolve("OrderService", configValues);

        Assert.Equal("http://identity-service", options.Authority);
        Assert.Equal("b-service", options.ClientId);
        Assert.Equal("s3cret", options.ClientSecret);
        Assert.Equal("order-api", options.Scope);
    }

    [Fact]
    public void Client_without_scope_inherits_the_global_default()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:Authority"] = "http://identity-service",
            ["Leistd:ServiceAuth:ClientId"] = "b-service",
            ["Leistd:ServiceAuth:ClientSecret"] = "s3cret",
            ["Leistd:ServiceAuth:Scope"] = "default-scope",
            ["Leistd:ServiceClients:PlainService:BaseAddress"] = "http://plain-service",
        };

        var options = Resolve("PlainService", configValues);

        Assert.Equal("default-scope", options.Scope);
    }

    [Fact]
    public void Client_scope_overrides_the_global_default()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:Authority"] = "http://identity-service",
            ["Leistd:ServiceAuth:ClientId"] = "b-service",
            ["Leistd:ServiceAuth:ClientSecret"] = "s3cret",
            ["Leistd:ServiceAuth:Scope"] = "default-scope",
            ["Leistd:ServiceClients:OrderService:Scope"] = "order-api",
        };

        var options = Resolve("OrderService", configValues);

        Assert.Equal("order-api", options.Scope);
    }

    [Fact]
    public void Scope_stays_empty_when_neither_global_nor_client_sets_it()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:Authority"] = "http://identity-service",
            ["Leistd:ServiceAuth:ClientId"] = "b-service",
            ["Leistd:ServiceAuth:ClientSecret"] = "s3cret",
        };

        var options = Resolve("OrderService", configValues);

        Assert.Null(options.Scope);
    }
}
