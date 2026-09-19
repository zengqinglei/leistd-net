using System.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Middlewares;
using Leistd.ServiceClient.AspNetCore.Options;
using Leistd.Security.Claims;
using Leistd.ServiceClient.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Leistd.TestBase.Doubles;

namespace Leistd.ServiceClient.Tests.AspNetCore;

public class ServiceUserContextMiddlewareTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<HttpContext> RunAsync(
        ClaimsPrincipal user,
        Action<HttpContext>? configureRequest = null,
        Action<ServiceUserContextOptions>? configureOptions = null)
    {
        var options = new ServiceUserContextOptions();
        configureOptions?.Invoke(options);
        var middleware = new ServiceUserContextMiddleware(
            _ => Task.CompletedTask,
            new MutableOptionsMonitor<ServiceUserContextOptions>(options),
            NullLogger<ServiceUserContextMiddleware>.Instance);

        var context = new DefaultHttpContext { User = user };
        configureRequest?.Invoke(context);
        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task Enabled_switch_is_read_for_each_request()
    {
        var monitor = new MutableOptionsMonitor<ServiceUserContextOptions>(new());
        var middleware = new ServiceUserContextMiddleware(
            _ => Task.CompletedTask,
            monitor,
            NullLogger<ServiceUserContextMiddleware>.Instance);

        var first = new DefaultHttpContext { User = ServiceClientPrincipal() };
        AddUserHeaders(first);
        await middleware.InvokeAsync(first);

        monitor.Set(new ServiceUserContextOptions { Enabled = false });
        var second = new DefaultHttpContext { User = ServiceClientPrincipal() };
        AddUserHeaders(second);
        await middleware.InvokeAsync(second);

        monitor.Set(new ServiceUserContextOptions { Enabled = true });
        var third = new DefaultHttpContext { User = ServiceClientPrincipal() };
        AddUserHeaders(third);
        await middleware.InvokeAsync(third);

        Assert.Equal(UserId.ToString(), first.User.FindFirst("sub")?.Value);
        Assert.Equal(ClientSubject.Format("svc-a"), second.User.FindFirst("sub")?.Value);
        Assert.Equal(UserId.ToString(), third.User.FindFirst("sub")?.Value);
    }

    /// <summary>client credentials 主体：sub 为 ClientSubject 契约形态（client:&lt;client_id&gt;）。</summary>
    private static ClaimsPrincipal ServiceClientPrincipal(string clientId = "svc-a", params Claim[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new("sub", ClientSubject.Format(clientId)),
            new("client_id", clientId),
            new("scope", ServiceClientScopes.Delegation),   // 默认要求委托 scope
        };
        claims.AddRange(extraClaims);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestBearer"));
    }

    /// <summary>普通用户 token 主体：sub 是用户 Id（GUID），不是机器主体形态。</summary>
    private static ClaimsPrincipal UserTokenPrincipal() =>
        new(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("client_id", "web-app")],
            "TestBearer"));

    private static void AddUserHeaders(HttpContext context)
    {
        context.Request.Headers[ServiceClientHeaders.UserId] = UserId.ToString();
        context.Request.Headers[ServiceClientHeaders.Username] = Uri.EscapeDataString("张三");
    }

    [Fact]
    public async Task Trusted_service_call_restores_the_user_and_keeps_the_client_identity()
    {
        var context = await RunAsync(ServiceClientPrincipal(), AddUserHeaders);

        Assert.Equal(UserId.ToString(), context.User.FindFirst("sub")?.Value);
        Assert.Equal("张三", context.User.FindFirst("preferred_username")?.Value);
        Assert.Equal("svc-a", context.User.FindFirst("client_id")?.Value);
        Assert.Equal("ServiceUserContext", context.User.Identity?.AuthenticationType);
        Assert.Equal(2, context.User.Identities.Count()); // 用户身份 + client 身份
    }

    [Fact]
    public async Task Trusted_service_call_without_user_header_keeps_the_client_principal()
    {
        var context = await RunAsync(ServiceClientPrincipal());

        Assert.Equal(ClientSubject.Format("svc-a"), context.User.FindFirst("sub")?.Value);
        Assert.Single(context.User.Identities);
    }

    [Fact]
    public async Task User_token_with_user_header_is_untrusted_and_header_is_stripped()
    {
        var context = await RunAsync(UserTokenPrincipal(), AddUserHeaders);

        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.Username));
        Assert.NotEqual(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Anonymous_request_user_header_is_stripped()
    {
        var context = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity()), AddUserHeaders);

        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
    }

    [Fact]
    public async Task Stripping_disabled_keeps_header_without_restoring_the_user()
    {
        var context = await RunAsync(
            UserTokenPrincipal(), AddUserHeaders,
            options => options.RemoveUntrustedHeaders = false);

        Assert.True(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.NotEqual(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Required_scope_matches_the_standard_scope_claim()
    {
        var context = await RunAsync(
            ServiceClientPrincipal("svc-a", new Claim("scope", "other svc.call")),
            AddUserHeaders,
            options => options.RequiredScope = "svc.call");

        Assert.Equal(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Required_scope_matches_the_openiddict_scope_claim()
    {
        var context = await RunAsync(
            ServiceClientPrincipal("svc-a", new Claim("oi_scp", "svc.call")),
            AddUserHeaders,
            options => options.RequiredScope = "svc.call");

        Assert.Equal(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Missing_required_scope_is_untrusted()
    {
        var context = await RunAsync(
            ServiceClientPrincipal(),
            AddUserHeaders,
            options => options.RequiredScope = "svc.call");

        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.Equal(ClientSubject.Format("svc-a"), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Machine_token_without_the_delegation_scope_cannot_act_as_a_user()
    {
        // 安全默认（fail-closed）：仅有 client_credentials 能力、未获委托 scope 的客户端，
        // 即使知道用户 Id 也无法恢复成该用户。
        var withoutDelegation = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", ClientSubject.Format("svc-a")), new Claim("client_id", "svc-a")],
            "TestBearer"));

        var context = await RunAsync(withoutDelegation, AddUserHeaders);

        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.Equal(ClientSubject.Format("svc-a"), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Machine_token_with_the_delegation_scope_restores_the_user()
    {
        var context = await RunAsync(ServiceClientPrincipal(), AddUserHeaders);

        Assert.Equal(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Custom_header_claim_mapping_is_applied_on_restore()
    {
        var context = await RunAsync(
            ServiceClientPrincipal(),
            context =>
            {
                AddUserHeaders(context);
                context.Request.Headers["X-Tenant"] = Uri.EscapeDataString("租户A");
            },
            options => options.HeaderClaimMap["X-Tenant"] = "tenant");

        Assert.Equal("租户A", context.User.FindFirst("tenant")?.Value);
    }

    [Fact]
    public async Task Disabled_feature_neither_restores_nor_strips()
    {
        var context = await RunAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            AddUserHeaders,
            options => options.Enabled = false);

        Assert.True(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
    }
}
