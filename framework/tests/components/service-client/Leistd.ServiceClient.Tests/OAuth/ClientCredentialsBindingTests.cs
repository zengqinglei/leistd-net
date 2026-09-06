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

    [Fact]
    public void 全局身份一次配置_多个客户端共享_Scope按目标服务区分()
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
    public void 客户端未配置Scope_继承全局默认Scope()
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
    public void 客户端Scope覆盖全局默认Scope()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:ClientId"] = "b-service",
            ["Leistd:ServiceAuth:Scope"] = "default-scope",
            ["Leistd:ServiceClients:OrderService:Scope"] = "order-api",
        };

        var options = Resolve("OrderService", configValues);

        Assert.Equal("order-api", options.Scope);
    }

    [Fact]
    public void 无全局Scope也无客户端Scope_保持为空()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:ClientId"] = "b-service",
        };

        var options = Resolve("OrderService", configValues);

        Assert.Null(options.Scope);
    }
}
