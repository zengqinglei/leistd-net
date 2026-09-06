using Leistd.Tracing.AspNetCore.Middlewares;
using Leistd.Tracing.Abstractions;
using Leistd.Tracing.HttpClient.Handlers;
using Leistd.Tracing.Options;
using Leistd.Tracing.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Leistd.TestBase.Doubles;

namespace Leistd.Tracing.Tests.Core;

public sealed class CorrelationIdOptionsTests
{
    [Fact]
    public async Task Middleware_reads_the_enabled_switch_for_each_request()
    {
        var monitor = new MutableOptionsMonitor<CorrelationIdOptions>(new());
        var middleware = CreateMiddleware(monitor);
        var provider = new CorrelationIdProvider();

        var first = ContextWithHeader("first-id");
        await middleware.InvokeAsync(first, provider);
        Assert.Equal("first-id", first.TraceIdentifier);

        monitor.Set(new CorrelationIdOptions { Enabled = false });
        var second = ContextWithHeader("second-id");
        second.TraceIdentifier = "unchanged";
        await middleware.InvokeAsync(second, provider);
        Assert.Equal("unchanged", second.TraceIdentifier);

        monitor.Set(new CorrelationIdOptions { Enabled = true });
        var third = ContextWithHeader("third-id");
        await middleware.InvokeAsync(third, provider);
        Assert.Equal("third-id", third.TraceIdentifier);
    }

    [Fact]
    public async Task Outbound_handler_reads_the_enabled_switch_for_each_request()
    {
        var monitor = new MutableOptionsMonitor<CorrelationIdOptions>(new());
        var provider = new CorrelationIdProvider();
        var capture = new CapturingHttpMessageHandler();
        var handler = new CorrelationIdDelegatingHandler(provider, monitor) { InnerHandler = capture };
        using var invoker = new HttpMessageInvoker(handler);
        using var correlation = provider.Change("trace-id");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/one"), default);

        monitor.Set(new CorrelationIdOptions { Enabled = false });
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/two"), default);

        monitor.Set(new CorrelationIdOptions { Enabled = true });
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://demo/three"), default);

        Assert.True(capture.Requests[0].Headers.Contains("X-Correlation-Id"));
        Assert.False(capture.Requests[1].Headers.Contains("X-Correlation-Id"));
        Assert.True(capture.Requests[2].Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public void Header_names_bind_as_a_collection_and_are_normalized()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Leistd:CorrelationId:HeaderNames:0"] = " X-Correlation-Id ",
                ["Leistd:CorrelationId:HeaderNames:1"] = "",
                ["Leistd:CorrelationId:HeaderNames:2"] = "x-correlation-id",
                ["Leistd:CorrelationId:HeaderNames:3"] = "X-Request-Id"
            })
            .Build();
        using var serviceProvider = new ServiceCollection()
            .AddCorrelationIdCore(configuration)
            .BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<CorrelationIdOptions>>().Value;

        Assert.Equal(["X-Correlation-Id", "X-Request-Id"], options.HeaderNames);
    }

    [Fact]
    public void Empty_header_names_are_rejected()
    {
        using var serviceProvider = new ServiceCollection()
            .AddCorrelationIdCore(options => options.HeaderNames = ["", " "])
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            serviceProvider.GetRequiredService<IOptions<CorrelationIdOptions>>().Value);
    }

    [Fact]
    public async Task Response_header_switch_uses_the_current_request_snapshot()
    {
        var monitor = new MutableOptionsMonitor<CorrelationIdOptions>(new());
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IOptionsMonitor<CorrelationIdOptions>>(monitor);
                    services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();
                })
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.Run(_ => Task.CompletedTask);
                }))
            .StartAsync();
        using var client = host.GetTestClient();

        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        firstRequest.Headers.Add("X-Correlation-Id", "first-id");
        using var first = await client.SendAsync(firstRequest);
        Assert.Equal("first-id", first.Headers.GetValues("X-Correlation-Id").Single());

        monitor.Set(new CorrelationIdOptions { IncludeInResponseHeaders = false });
        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        secondRequest.Headers.Add("X-Correlation-Id", "second-id");
        using var second = await client.SendAsync(secondRequest);
        Assert.False(second.Headers.Contains("X-Correlation-Id"));
    }

    private static CorrelationIdMiddleware CreateMiddleware(
        IOptionsMonitor<CorrelationIdOptions> monitor) =>
        new(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance, monitor);

    private static DefaultHttpContext ContextWithHeader(string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = value;
        return context;
    }

}
