using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Api.Middlewares;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class BrowserOriginTests
{
    [Theory]
    [InlineData("https://localhost", false)]
    [InlineData("https://allowed.test", false)]
    [InlineData("https://untrusted.test", true)]
    [InlineData("null", true)]
    public async Task Origin_checks_share_the_Cors_policy_and_explain_rejections(string origin, bool denied)
    {
        using var factory = new ProjectWebApplicationFactory();
        var logger = new OriginLogger();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Cors:AllowedOrigins:0", "https://allowed.test");
            builder.ConfigureTestServices(services => services.AddSingleton<ILogger<BrowserOriginMiddleware>>(logger));
        });
        using var browser = ProjectWebApplicationFactory.CreateProjectClient(host);
        browser.BaseAddress = new Uri("https://localhost");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/unknown-origin-probe");
        request.Headers.Add("Origin", origin);
        using var response = await browser.SendAsync(request);
        Assert.Equal(denied ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, response.StatusCode);
        if (!denied) { Assert.Empty(logger.Warnings); return; }
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(403, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
        Assert.Contains(logger.Warnings, message => message.Contains(origin, StringComparison.Ordinal) &&
            message.Contains("https://localhost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trusted_tls_forwarding_restores_the_browser_origin()
    {
        using var factory = new ProjectWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ForwardedHeaders:KnownProxies:0", "127.0.0.1"));
        using var browser = ProjectWebApplicationFactory.CreateProjectClient(host);
        browser.BaseAddress = new Uri("http://localhost");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/unknown-origin-probe");
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class OriginLogger : ILogger<BrowserOriginMiddleware>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
