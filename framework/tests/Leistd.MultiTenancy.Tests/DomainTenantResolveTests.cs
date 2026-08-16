using System.Security.Claims;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

/// <summary>
/// 子域名解析：格式匹配规则，以及它在解析链中的位置。
/// </summary>
public class DomainTenantResolveTests : IAsyncLifetime
{
    private static readonly Guid AcmeId = Guid.NewGuid();
    private static readonly Guid GlobexId = Guid.NewGuid();

    private IHost _host = default!;
    private HttpClient _client = default!;

    /// <summary>
    /// 主机名到租户的映射规则，全部走真实请求——格式匹配是这个贡献者的对外契约，
    /// 不是可以只在内部函数上验的实现细节。
    /// </summary>
    [Theory]
    // 主机名大小写不敏感（RFC 4343）
    [InlineData("http://ACME.Example.COM/", "acme")]
    // 端口不参与匹配：格式只写主机名，开发（:5240）与生产共用一份配置
    [InlineData("http://acme.example.com:5240/", "acme")]
    // 恰好是基础域：宿主入口，不是某个租户
    [InlineData("http://example.com/", "host")]
    // 域名不属于本格式：不解析，交给链上后续贡献者
    [InlineData("http://acme.other.com/", "host")]
    // 多级子域不应被当成名为 "a.b" 的租户
    [InlineData("http://a.b.example.com/", "host")]
    public async Task Host_maps_to_a_tenant_only_when_it_fits_the_format(string url, string expected)
    {
        var body = await GetAsync(url);
        Assert.Equal(expected == "host" ? "host" : AcmeId.ToString(), body);
    }

    [Fact]
    public async Task Subdomain_resolves_the_tenant_without_any_header()
    {
        Assert.Equal(AcmeId.ToString(), await GetAsync("http://acme.example.com/"));
        Assert.Equal(GlobexId.ToString(), await GetAsync("http://globex.example.com/"));
    }

    /// <summary>
    /// 子域名排在请求头之前：子域名部署下域名是权威，匿名请求不能用头把自己挪到别的租户。
    /// </summary>
    [Fact]
    public async Task Header_cannot_move_an_anonymous_request_off_the_subdomain()
    {
        var body = await GetAsync(
            "http://acme.example.com/",
            ("X-Tenant-Id", GlobexId.ToString()));

        Assert.Equal(AcmeId.ToString(), body);
    }

    /// <summary>
    /// claim 仍然优先于子域名：已认证主体的租户由 claim 定案，这条顺序是防跨租户越权的关键。
    /// </summary>
    [Fact]
    public async Task Authenticated_principal_claim_still_wins_over_the_subdomain()
    {
        var body = await GetAsync(
            "http://acme.example.com/",
            ("X-Test-Auth", "user-1"),
            ("X-Test-Tenant-Claim", GlobexId.ToString()));

        Assert.Equal(GlobexId.ToString(), body);
    }

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddMultiTenancy(options => options.DomainFormat = "{0}.example.com");
                    services.AddInMemoryTenantStore(options =>
                    {
                        options.Tenants.Add(new TenantConfiguration
                        {
                            Id = AcmeId,
                            Name = "acme",
                            NormalizedName = "ACME"
                        });
                        options.Tenants.Add(new TenantConfiguration
                        {
                            Id = GlobexId,
                            Name = "globex",
                            NormalizedName = "GLOBEX"
                        });
                    });
                })
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Test-Auth", out var user))
                        {
                            var claims = new List<Claim> { new("sub", user.ToString()) };
                            if (context.Request.Headers.TryGetValue("X-Test-Tenant-Claim", out var tenantClaim))
                            {
                                claims.Add(new Claim(CustomClaimTypes.TenantId, tenantClaim.ToString()));
                            }

                            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
                        }

                        await next(context);
                    });

                    app.UseMultiTenancy();

                    app.Run(async context =>
                    {
                        var currentTenant = context.RequestServices.GetRequiredService<ICurrentTenant>();
                        await context.Response.WriteAsync(currentTenant.Id?.ToString() ?? "host");
                    });
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<string> GetAsync(string url, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        var response = await _client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }
}
