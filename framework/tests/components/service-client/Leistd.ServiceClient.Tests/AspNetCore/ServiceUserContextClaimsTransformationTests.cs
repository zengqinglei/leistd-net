using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Claims;
using Leistd.ServiceClient.AspNetCore.Options;
using Leistd.ServiceClient.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Leistd.TestBase.Doubles;

namespace Leistd.ServiceClient.Tests.AspNetCore;

public class ServiceUserContextClaimsTransformationTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<ClaimsPrincipal> TransformAsync(
        ClaimsPrincipal principal,
        Action<IHeaderDictionary>? configureHeaders = null,
        Action<ServiceUserContextOptions>? configureOptions = null,
        bool withHttpContext = true,
        ClaimTypeOptions? claimTypes = null)
    {
        var options = new ServiceUserContextOptions();
        configureOptions?.Invoke(options);

        var accessor = new HttpContextAccessor();
        if (withHttpContext)
        {
            var context = new DefaultHttpContext();
            configureHeaders?.Invoke(context.Request.Headers);
            accessor.HttpContext = context;
        }

        var transformation = new ServiceUserContextClaimsTransformation(
            accessor,
            new MutableOptionsMonitor<ServiceUserContextOptions>(options),
            Microsoft.Extensions.Options.Options.Create(claimTypes ?? new ClaimTypeOptions()),
            NullLogger<ServiceUserContextClaimsTransformation>.Instance);
        return await transformation.TransformAsync(principal);
    }

    // 还原写入的租户 claim 与读取方同一类型：宿主改了名，被调方按新名写，解析与判权按新名读
    [Fact]
    public async Task The_restored_tenant_uses_the_configured_claim_type()
    {
        var tenantId = Guid.NewGuid();
        var restored = await TransformAsync(
            ServiceClientPrincipal(),
            headers => headers[ServiceClientHeaders.TenantId] = tenantId.ToString(),
            claimTypes: new ClaimTypeOptions { TenantId = "tid" });

        var claimTypes = new ClaimTypeOptions { TenantId = "tid" };
        Assert.Equal(new TenantClaim(true, tenantId), claimTypes.ReadTenant(restored));
        Assert.Null(restored.FindFirst(CustomClaimTypes.TenantId));
    }

    // 调用方身份自带租户声明时，代表的租户取代它，而不是两条并存（并存即非法，整个请求被拒）
    [Fact]
    public async Task The_restored_tenant_replaces_the_callers_own_tenant_claim()
    {
        var delegated = Guid.NewGuid();
        var caller = ServiceClientPrincipal();
        ((ClaimsIdentity)caller.Identity!).AddClaim(new Claim(CustomClaimTypes.TenantId, Guid.NewGuid().ToString()));

        var restored = await TransformAsync(caller, headers => headers[ServiceClientHeaders.TenantId] = delegated.ToString());

        Assert.Equal(new TenantClaim(true, delegated), new ClaimTypeOptions().ReadTenant(restored));
        Assert.NotNull(restored.FindFirst(CustomClaimTypes.ClientId));
    }

    [Fact]
    public async Task Enabled_switch_is_read_for_each_transformation()
    {
        var monitor = new MutableOptionsMonitor<ServiceUserContextOptions>(new());
        var context = new DefaultHttpContext();
        AddUserHeaders(context.Request.Headers);
        var transformation = new ServiceUserContextClaimsTransformation(
            new HttpContextAccessor { HttpContext = context },
            monitor,
            Microsoft.Extensions.Options.Options.Create(new Leistd.Security.Claims.ClaimTypeOptions()),
            NullLogger<ServiceUserContextClaimsTransformation>.Instance);

        var first = await transformation.TransformAsync(ServiceClientPrincipal());
        monitor.Set(new ServiceUserContextOptions { Enabled = false });
        var secondPrincipal = ServiceClientPrincipal();
        var second = await transformation.TransformAsync(secondPrincipal);
        monitor.Set(new ServiceUserContextOptions { Enabled = true });
        var third = await transformation.TransformAsync(ServiceClientPrincipal());

        Assert.Equal(UserId.ToString(), first.FindFirst("sub")?.Value);
        Assert.Same(secondPrincipal, second);
        Assert.Equal(UserId.ToString(), third.FindFirst("sub")?.Value);
    }

    private static ClaimsPrincipal ServiceClientPrincipal(string clientId = "svc-a") =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", ClientSubject.Format(clientId)),
                new Claim("client_id", clientId),
                new Claim("scope", ServiceClientScopes.Delegation),
            ],
            "TestBearer"));

    private static void AddUserHeaders(IHeaderDictionary headers)
    {
        headers[ServiceClientHeaders.UserId] = UserId.ToString();
        headers[ServiceClientHeaders.Username] = Uri.EscapeDataString("张三");
    }

    [Fact]
    public async Task Trusted_service_principal_restores_the_user_during_authentication()
    {
        var result = await TransformAsync(ServiceClientPrincipal(), AddUserHeaders);

        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
        Assert.Equal("张三", result.FindFirst("preferred_username")?.Value);
        Assert.Equal("svc-a", result.FindFirst("client_id")?.Value);
        Assert.Equal(2, result.Identities.Count());
    }

    [Fact]
    public async Task Already_restored_principal_is_returned_unchanged()
    {
        var first = await TransformAsync(ServiceClientPrincipal(), AddUserHeaders);

        var second = await TransformAsync(first, AddUserHeaders);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Non_service_principal_is_left_alone()
    {
        var userToken = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("client_id", "web-app")],
            "TestBearer"));

        var result = await TransformAsync(userToken, AddUserHeaders);

        Assert.Same(userToken, result);
    }

    [Fact]
    public async Task Missing_http_context_returns_the_principal_unchanged()
    {
        var principal = ServiceClientPrincipal();

        var result = await TransformAsync(principal, withHttpContext: false);

        Assert.Same(principal, result);
    }

    [Fact]
    public async Task Missing_user_header_returns_the_principal_unchanged()
    {
        var principal = ServiceClientPrincipal();

        var result = await TransformAsync(principal);

        Assert.Same(principal, result);
    }

    [Fact]
    public async Task Disabled_feature_returns_the_principal_unchanged()
    {
        var principal = ServiceClientPrincipal();

        var result = await TransformAsync(
            principal, AddUserHeaders, options => options.Enabled = false);

        Assert.Same(principal, result);
    }
}
