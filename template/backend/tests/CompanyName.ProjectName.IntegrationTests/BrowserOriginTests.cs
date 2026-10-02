using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Api.Middlewares;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 来源检查的用例共用一个派生宿主（同一份 CORS 配置与日志替身）；同类用例顺序执行，每例开头清空日志。
/// </summary>
public sealed class BrowserOriginTests(BrowserOriginTests.OriginHost origin) : IClassFixture<BrowserOriginTests.OriginHost>
{
    [Theory]
    [InlineData("https://localhost", false)]
    [InlineData("https://allowed.test", false)]
    [InlineData("https://untrusted.test", true)]
    [InlineData("null", true)]
    public async Task Origin_checks_share_the_Cors_policy_and_explain_rejections(string origin, bool denied)
    {
        using var response = await SendWriteAsync(request => request.Headers.Add("Origin", origin));
        Assert.Equal(denied ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, response.StatusCode);
        var warnings = Warnings();
        if (!denied) { Assert.Empty(warnings); return; }
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(403, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
        Assert.Contains(warnings, message => message.Contains(origin, StringComparison.Ordinal) &&
            message.Contains("https://localhost", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cross-site", true)]
    [InlineData("same-site", true)]
    [InlineData("same-origin", false)]
    [InlineData("none", false)]
    [InlineData(null, false)]
    public async Task Without_an_origin_fetch_metadata_decides_and_non_browser_calls_pass(string? site, bool denied)
    {
        using var response = await SendWriteAsync(request =>
        {
            if (site is not null) request.Headers.Add("Sec-Fetch-Site", site);
        });
        Assert.Equal(denied ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, response.StatusCode);
        var warnings = Warnings();
        if (denied) Assert.Contains(warnings, message => message.Contains(site!, StringComparison.Ordinal));
        else Assert.Empty(warnings);
    }

    [Fact]
    public async Task Fetch_metadata_cannot_override_a_rejected_origin()
    {
        using var response = await SendWriteAsync(request =>
        {
            request.Headers.Add("Origin", "https://untrusted.test");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

#if (LocalIdentity)
    [Fact]
    public async Task A_rejected_cross_site_write_has_no_side_effect()
    {
        // 每例自己登录，会话互不影响；被拒的退出请求不能结束这个会话。
        using var session = await ProjectWebApplicationFactory.LoginAsync(origin.Host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var response = await session.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

#endif
    [Fact]
    public async Task Trusted_tls_forwarding_restores_the_browser_origin()
    {
        // 受信任代理是另一份配置，单独派生宿主。
        using var host = origin.Factory.WithWebHostBuilder(builder => builder.UseSetting("ForwardedHeaders:KnownProxies:0", "127.0.0.1"));
        using var browser = ProjectWebApplicationFactory.CreateProjectClient(host);
        browser.BaseAddress = new Uri("http://localhost");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/unknown-origin-probe");
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendWriteAsync(Action<HttpRequestMessage> configure)
    {
        origin.Logger.Collector.Clear();
        using var browser = ProjectWebApplicationFactory.CreateProjectClient(origin.Host);
        browser.BaseAddress = new Uri("https://localhost");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/unknown-origin-probe");
        configure(request);
        return await browser.SendAsync(request);
    }

    private List<string> Warnings() =>
        origin.Logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).Select(record => record.Message).ToList();

    /// <summary>本类共用的宿主：允许一个额外前端源，来源检查的日志写入 <see cref="FakeLogger{T}"/>。</summary>
    public sealed class OriginHost : IDisposable
    {
        public OriginHost()
        {
            Host = Factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Cors:AllowedOrigins:0", "https://allowed.test");
                builder.ConfigureTestServices(services => services.AddSingleton<ILogger<BrowserOriginMiddleware>>(Logger));
            });
        }

        public ProjectWebApplicationFactory Factory { get; } = new();
        public FakeLogger<BrowserOriginMiddleware> Logger { get; } = new();
        public WebApplicationFactory<Program> Host { get; }

        public void Dispose()
        {
            Host.Dispose();
            Factory.Dispose();
        }
    }
}
