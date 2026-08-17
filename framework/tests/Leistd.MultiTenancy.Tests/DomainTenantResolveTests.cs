using System.Security.Claims;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    /// 末尾根点是 DNS 中的等价写法，不能成为绕过子域名权威的后门。
    /// </summary>
    /// <remarks>
    /// <c>acme.example.com.</c> 与 <c>acme.example.com</c> 在 DNS 中是同一个名字，
    /// 而 <c>HostString.Host</c> 会原样保留那个点。字面比较匹配不上就会退回请求头，
    /// 于是匿名请求只要在 Host 末尾多打一个点，就能用 X-Tenant-Id 挑任意租户。
    /// 配置侧要求不带根点（唯一 canonical 形态），请求侧则必须规范化后再匹配。
    /// </remarks>
    [Theory]
    [InlineData("acme.example.com.")]
    [InlineData("acme.example.com.:5240")]
    // 去掉的是全部末尾点而非恰好一个：只去一个的话，多打一个点绕过又回来了
    [InlineData("acme.example.com..")]
    public async Task Trailing_root_dot_in_the_host_is_not_a_bypass(string hostHeader)
    {
        // 必须显式写 Host 头：HttpClient 在构造 URI 时就会把末尾点规范化掉，
        // 用带点的 URL 根本打不到这条路径——而攻击者是在报文里直接写这个头的
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://acme.example.com/");
        // 不走校验的 setter：攻击者是在报文里直接写这个头的，
        // HttpRequestHeaders.Host 的校验只是客户端的礼貌，不是服务端的保证
        request.Headers.TryAddWithoutValidation("Host", hostHeader);
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", GlobexId.ToString());

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

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

    /// <summary>
    /// 非法 DomainFormat 必须在启动期失败，而不是运行期静默退回请求头解析。
    /// </summary>
    /// <remarks>
    /// 这是 fail-open 的典型形态：配置写错了，系统照常启动、看起来在跑，
    /// 而"子域名是权威来源"这条边界已经没了——请求头重新说了算。
    /// </remarks>
    [Theory]
    [InlineData("example.com")]                 // 漏了占位符
    [InlineData("{0}.{0}.example.com")]         // 多个占位符
    [InlineData("https://{0}.example.com")]     // 带 scheme
    [InlineData("{0}.example.com/app")]         // 带路径
    [InlineData("{0}.example.com:5240")]        // 带端口
    [InlineData("{0}. example.com")]            // 含空白
    [InlineData("{0}.example.com?source=x")]    // 带查询串
    [InlineData("{0}.example.com#fragment")]    // 带片段
    [InlineData("user@{0}.example.com")]        // 带用户信息
    [InlineData("{0}..example.com")]            // 空 label
    [InlineData("{0}.{1}.example.com")]         // 混入其它占位符
    [InlineData("{0}\\example.com")]            // 反斜杠
    [InlineData("{0}_.example.com")]            // 下划线：CheckHostName 放行，浏览器不会这么发
    [InlineData("{0}-.example.com")]            // 段尾连字符
    [InlineData("{0}.example-.com")]            // 同上，出现在后缀段
    [InlineData("{0}.bücher.example")]          // Unicode：应要求填 punycode
    [InlineData("{0}")]                         // 只有一段
    [InlineData("{0}.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.com")]  // 段超 63
    public async Task Invalid_domain_format_stops_the_host_from_starting(string format)
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost => webHost
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddMultiTenancy(options => options.DomainFormat = format);
                services.AddInMemoryTenantStore(_ => { });
            })
            .Configure(app => app.UseMultiTenancy()));

        var error = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            using var host = await builder.StartAsync();
        });

        Assert.Contains("DomainFormat", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 占位符不必占满整个 label：<c>tenant-{0}.example.com</c> 是合法形态。
    /// </summary>
    /// <remarks>
    /// 替换后仍是合法主机名即可；租户段本身不含点号由运行期解析保证。
    /// </remarks>
    [Theory]
    [InlineData("{0}.example.com")]
    [InlineData("tenant-{0}.example.com")]
    [InlineData("{0}.app.example.com")]
    [InlineData("{0}.xn--bcher-kva.example")]   // punycode 形态：与浏览器发送的 Host 一致
    public async Task Well_formed_domain_formats_start_normally(string format)
    {
        using var host = await new HostBuilder().ConfigureWebHost(webHost => webHost
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddMultiTenancy(options => options.DomainFormat = format);
                services.AddInMemoryTenantStore(_ => { });
            })
            .Configure(app => app.UseMultiTenancy())).StartAsync();

        await host.StopAsync();
    }

    [Fact]
    public async Task Absent_domain_format_is_valid_and_simply_skips_the_contributor()
    {
        using var host = await new HostBuilder().ConfigureWebHost(webHost => webHost
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddMultiTenancy();
                services.AddInMemoryTenantStore(_ => { });
            })
            .Configure(app => app.UseMultiTenancy())).StartAsync();

        await host.StopAsync();
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
