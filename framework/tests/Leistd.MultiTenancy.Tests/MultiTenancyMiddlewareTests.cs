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
/// 多租户中间件端到端：解析链优先级、Store 校验失败语义、Change 覆盖下游管道。
/// </summary>
public class MultiTenancyMiddlewareTests : IAsyncLifetime
{
    private static readonly Guid ActiveTenantId = Guid.NewGuid();
    private static readonly Guid InactiveTenantId = Guid.NewGuid();
    private static readonly Guid ClaimTenantId = ActiveTenantId;

    private IHost _host = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddMultiTenancy();
                    services.AddInMemoryTenantStore(options =>
                    {
                        options.Tenants.Add(new TenantConfiguration
                        {
                            Id = ActiveTenantId,
                            Name = "acme",
                            NormalizedName = "ACME"
                        });
                        options.Tenants.Add(new TenantConfiguration
                        {
                            Id = InactiveTenantId,
                            Name = "frozen",
                            NormalizedName = "FROZEN",
                            IsActive = false
                        });
                    });
                })
                .Configure(app =>
                {
                    // 模拟认证：请求头 X-Test-Auth 存在时构造已认证主体，
                    // X-Test-Tenant-Claim 存在时附 tenant_id claim
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

    private async Task<string> GetAsync(string path, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        var response = await _client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task No_hint_resolves_to_host()
    {
        Assert.Equal("host", await GetAsync("/"));
    }

    [Fact]
    public async Task Header_resolves_active_tenant_and_change_covers_pipeline()
    {
        var body = await GetAsync("/", ("X-Tenant-Id", ActiveTenantId.ToString()));
        Assert.Equal(ActiveTenantId.ToString(), body);
    }

    [Fact]
    public async Task Header_resolves_by_name_case_insensitively()
    {
        var body = await GetAsync("/", ("X-Tenant-Id", "AcMe"));
        Assert.Equal(ActiveTenantId.ToString(), body);
    }

    [Fact]
    public async Task Query_string_resolves_when_no_header()
    {
        var body = await GetAsync($"/?tenant={ActiveTenantId}");
        Assert.Equal(ActiveTenantId.ToString(), body);
    }

    [Fact]
    public async Task Unknown_tenant_throws_not_found()
    {
        // 未配置全局异常处理器的 TestServer 会把异常原样抛给调用方；
        // 真实宿主由 Leistd.Exception 映射为 404
        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => GetAsync("/", ("X-Tenant-Id", Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task Inactive_tenant_throws_not_active()
    {
        await Assert.ThrowsAsync<TenantNotActiveException>(
            () => GetAsync("/", ("X-Tenant-Id", InactiveTenantId.ToString())));
    }

    [Fact]
    public async Task Authenticated_claim_wins_over_header()
    {
        // 已登录用户的租户由 claim 定案，伪造头无法把请求挪进别的租户
        var body = await GetAsync("/",
            ("X-Test-Auth", "u1"),
            ("X-Test-Tenant-Claim", ClaimTenantId.ToString()),
            ("X-Tenant-Id", Guid.NewGuid().ToString()));

        Assert.Equal(ClaimTenantId.ToString(), body);
    }

    [Fact]
    public async Task Authenticated_user_without_claim_is_host_even_with_header()
    {
        // 宿主用户（无 tenant_id claim）同样有定论：头不能把宿主会话改写成租户
        var body = await GetAsync("/",
            ("X-Test-Auth", "host-admin"),
            ("X-Tenant-Id", ActiveTenantId.ToString()));

        Assert.Equal("host", body);
    }
}
