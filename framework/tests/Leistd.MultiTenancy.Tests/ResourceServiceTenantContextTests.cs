using System.Security.Claims;
using Leistd.MultiTenancy.AspNetCore.Resolution;
using Leistd.MultiTenancy.AspNetCore;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Leistd.MultiTenancy.Exceptions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.Tests;

/// <summary>
/// 资源服务形态（<c>ValidateResolvedTenant = false</c>）：不持有租户注册表，
/// 租户上下文只来自已验证令牌的 claim。
/// </summary>
/// <remarks>
/// <para>本形态与持有注册表的宿主<b>共用同一个中间件</b>，只靠一个选项区分。
/// 为资源服务另建平行中间件（要求每个非匿名请求必须带恰好一个 <c>tenant_id</c>、否则 401）
/// 会比主流多租户框架更严，代价是平台运维端点没有落点——只能标成公开、搬去 Identity，
/// 或再引入一个端点豁免特性。而"有 claim 即租户、无 claim 即宿主"这套语义
/// 由 <see cref="CurrentPrincipalTenantResolveContributor"/> 加中间件的 null 分支承担，
/// 也是主流多租户框架的默认语义。</para>
/// <para>边界改由权限定义的 <c>MultiTenancySides</c> 承担：宿主上下文只能看到宿主行
/// （过滤器仍然生效，只是按 <c>TenantId IS NULL</c>），跨租户操作需要 Host 侧权限。</para>
/// </remarks>
public class ResourceServiceTenantContextTests : IAsyncLifetime
{
    private IHost _host = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                // 刻意不注册任何 ITenantStore：资源服务没有注册表，
                // 若中间件在本形态下仍去查存储，这里会直接抛出来
                .ConfigureServices(services => services.AddMultiTenancy(options =>
                {
                    options.ValidateResolvedTenant = false;
                    // 即便配了子域名格式也不该生效——链已被收窄
                    options.DomainFormat = "{0}.example.com";
                }))
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Test-Auth", out var subject))
                        {
                            var claims = new List<Claim> { new("sub", subject.ToString()) };
                            foreach (var value in context.Request.Headers["X-Test-Tenant-Claim"])
                            {
                                claims.Add(new Claim(CustomClaimTypes.TenantId, value!));
                            }

                            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
                        }

                        await next(context);
                    });

                    app.UseMultiTenancy();
                    app.Run(async context =>
                    {
                        var tenant = context.RequestServices.GetRequiredService<ICurrentTenant>();
                        await context.Response.WriteAsync(tenant.Id?.ToString() ?? "host");
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

    [Fact]
    public async Task Verified_claim_establishes_the_tenant_without_a_store()
    {
        var tenantId = Guid.NewGuid();

        var response = await SendAsync("/orders", subject: "alice", tenantClaims: [tenantId.ToString()]);

        Assert.Equal(tenantId.ToString(), response);
    }

    /// <summary>
    /// 已认证但无租户 claim：宿主上下文，不是错误。
    /// </summary>
    /// <remarks>
    /// 这一档是删掉平行中间件换来的。机器主体（客户端凭据）、宿主用户、平台运维端点
    /// 都落在这里；此前它们一律 401，控制面接口只能标成公开或搬去 Identity 服务。
    /// </remarks>
    [Fact]
    public async Task Authenticated_principal_without_a_tenant_claim_runs_as_host()
    {
        var response = await SendAsync("/metrics", subject: "platform-operator", tenantClaims: []);

        Assert.Equal("host", response);
    }

    [Fact]
    public async Task Anonymous_request_runs_as_host()
    {
        Assert.Equal("host", await SendAsync("/health", subject: null, tenantClaims: []));
    }

    /// <summary>
    /// <b>匿名</b>请求携带的租户线索不得被采信：链已收窄到只有主体贡献者。
    /// </summary>
    /// <remarks>
    /// <para>这是"跳过注册表校验"与"收窄解析链"必须成对的原因。两者脱钩时，
    /// 一个未认证的调用方只要带上 <c>X-Tenant-Id</c>（或 <c>?tenant=</c>、或访问租户子域名），
    /// 就能拿到该租户的上下文，而资源服务不查注册表、没有任何一道会拦下它。
    /// 匿名端点（登录、找回密码、邮箱验证）就此在攻击者指定的租户上下文里执行。</para>
    /// <para><b>必须用匿名请求测</b>：已认证请求会在
    /// <see cref="CurrentPrincipalTenantResolveContributor"/> 处 <c>Handled</c> 终止链，
    /// 后面的贡献者根本到不了，测不到收窄。</para>
    /// </remarks>
    [Theory]
    [InlineData("http://localhost/orders?tenant=11111111-1111-1111-1111-111111111111", null)]
    [InlineData("http://localhost/orders", "X-Tenant-Id")]
    [InlineData("http://acme.example.com/orders", null)]
    public async Task Unverified_tenant_hints_from_anonymous_requests_are_ignored(
        string url,
        string? headerName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headerName is not null)
        {
            request.Headers.Add(headerName, Guid.NewGuid().ToString());
        }

        var response = await _client.SendAsync(request);

        Assert.Equal("host", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 已认证请求的租户由 claim 定案，请求头无法改写。
    /// </summary>
    /// <remarks>
    /// 这条与上一条防的是不同的东西：本条靠贡献者顺序（主体在链首且终止链），
    /// 上一条靠链收窄。两道都要有——前者管已认证请求，后者管匿名请求。
    /// </remarks>
    [Fact]
    public async Task Header_cannot_override_a_verified_claim()
    {
        var claimTenant = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/orders");
        request.Headers.Add("X-Test-Auth", "alice");
        request.Headers.Add("X-Test-Tenant-Claim", claimTenant.ToString());
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal(claimTenant.ToString(), await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// claim 格式非法时拒绝，而不是静默退回宿主。
    /// </summary>
    /// <remarks>
    /// 令牌里带着一个解析不出的 <c>tenant_id</c> 是签发方或令牌被篡改的信号。
    /// 退回宿主意味着一个本应受租户约束的请求悄悄获得了宿主视角——按失败关闭处理。
    /// </remarks>
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Malformed_tenant_claim_is_rejected(string claimValue)
    {
        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => SendAsync("/orders", subject: "alice", tenantClaims: [claimValue]));
    }

    /// <summary>
    /// 多条租户 claim 一律失败关闭，即使两个值完全相同
    /// </summary>
    /// <remarks>
    /// <para>此前用的是 <c>FindFirst()</c>：两条 claim 静默取第一条。那是在安全边界上做静默选择——
    /// 攻击者只要能让令牌多出一条，就能决定后续所有租户过滤器、权限检查和写入落值的归属。</para>
    /// <para>值相同的那一档也必须拒绝。"反正结果一样所以无害"是拿当前实现的巧合当保证：
    /// 一旦哪天取的不是第一条，行为就变了；而且它会掩盖签发侧真实存在的缺陷——
    /// 一个主体带两条租户 claim，只可能来自签发方出错或令牌被拼接/篡改。</para>
    /// </remarks>
    [Fact]
    public async Task Duplicate_tenant_claims_with_different_values_are_rejected()
    {
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();

        var rejected = await Assert.ThrowsAsync<AmbiguousTenantClaimException>(
            () => SendAsync("/orders", subject: "alice", tenantClaims: [first, second]));

        Assert.Equal(2, rejected.Count);
        Assert.Equal(CustomClaimTypes.TenantId, rejected.ClaimType);
    }

    [Fact]
    public async Task Duplicate_tenant_claims_with_identical_values_are_also_rejected()
    {
        var tenantId = Guid.NewGuid().ToString();

        var rejected = await Assert.ThrowsAsync<AmbiguousTenantClaimException>(
            () => SendAsync("/orders", subject: "alice", tenantClaims: [tenantId, tenantId]));

        Assert.Equal(2, rejected.Count);
    }

    private async Task<string> SendAsync(string path, string? subject, string[] tenantClaims)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (subject is not null)
        {
            request.Headers.Add("X-Test-Auth", subject);
        }

        foreach (var claim in tenantClaims)
        {
            request.Headers.Add("X-Test-Tenant-Claim", claim);
        }

        var response = await _client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }
}
