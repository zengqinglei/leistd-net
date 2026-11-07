using Leistd.ServiceClient.Options;
using Leistd.TestBase.Doubles;
using Leistd.Tracing;
using Leistd.Tracing.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.ServiceClient.Tests.Core;

/// <summary><c>ForwardCorrelationId</c> 按命名客户端决定是否转发关联标识：默认转发，关掉的客户端不带该请求头，其他客户端不受影响。</summary>
/// <remarks>第三方不接收内部关联标识；关掉只影响该客户端，同一宿主的内部服务客户端照常转发。</remarks>
public sealed class CorrelationIdForwardingOptionTests
{
    private const string Header = "X-Correlation-Id";

    public interface IInternalClient
    {
        HttpClient Http { get; }
    }

    public interface IVendorClient
    {
        HttpClient Http { get; }
    }

    public sealed class InternalClient(HttpClient http) : IInternalClient
    {
        public HttpClient Http => http;
    }

    public sealed class VendorClient(HttpClient http) : IVendorClient
    {
        public HttpClient Http => http;
    }

    public sealed class InternalClientOptions : ServiceClientOptions;

    public sealed class VendorClientOptions : ServiceClientOptions;

    private static async Task<(string? Internal, string? Vendor)> SendBothAsync(
        Dictionary<string, string?> settings, Action<VendorClientOptions>? configureVendor = null)
    {
        var internalHandler = new CapturingHttpMessageHandler();
        var vendorHandler = new CapturingHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddCorrelationIdCore();
        services.AddServiceClient<IInternalClient, InternalClient, InternalClientOptions>(
                "Internal", options => options.BaseAddress = "http://internal")
            .ConfigurePrimaryHttpMessageHandler(() => internalHandler);
        services.AddServiceClient<IVendorClient, VendorClient, VendorClientOptions>("Vendor", configureVendor)
            .ConfigurePrimaryHttpMessageHandler(() => vendorHandler);

        await using var provider = services.BuildServiceProvider();
        var correlation = provider.GetRequiredService<ICorrelationIdProvider>();
        using (correlation.Change("corr-1"))
        {
            await provider.GetRequiredService<IInternalClient>().Http.GetAsync("api/ping");
            await provider.GetRequiredService<IVendorClient>().Http.GetAsync("https://vendor.example/api/ping");
        }

        return (HeaderOf(internalHandler), HeaderOf(vendorHandler));
    }

    private static string? HeaderOf(CapturingHttpMessageHandler handler) =>
        Assert.Single(handler.Requests).Headers.TryGetValues(Header, out var values) ? values.Single() : null;

    [Fact]
    public async Task Clients_forward_the_correlation_id_by_default()
    {
        var (internalHeader, vendorHeader) = await SendBothAsync([]);

        Assert.Equal("corr-1", internalHeader);
        Assert.Equal("corr-1", vendorHeader);
    }

    // 配置节与委托两个入口各覆盖一次：只覆盖一个时另一个改坏不会红
    [Fact]
    public async Task Disabling_forwarding_in_configuration_affects_only_that_client()
    {
        var (internalHeader, vendorHeader) = await SendBothAsync(new()
        {
            ["Leistd:ServiceClients:Vendor:ForwardCorrelationId"] = "false",
        });

        Assert.Equal("corr-1", internalHeader);
        Assert.Null(vendorHeader);
    }

    [Fact]
    public async Task Disabling_forwarding_in_the_delegate_affects_only_that_client()
    {
        var (internalHeader, vendorHeader) = await SendBothAsync([], options => options.ForwardCorrelationId = false);

        Assert.Equal("corr-1", internalHeader);
        Assert.Null(vendorHeader);
    }
}
