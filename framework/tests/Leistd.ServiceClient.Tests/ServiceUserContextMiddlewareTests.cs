using System.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Middlewares;
using Leistd.ServiceClient.AspNetCore.Options;
using Leistd.ServiceClient.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.ServiceClient.Tests;

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
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<ServiceUserContextMiddleware>.Instance);

        var context = new DefaultHttpContext { User = user };
        configureRequest?.Invoke(context);
        await middleware.InvokeAsync(context);
        return context;
    }

    /// <summary>client credentials 主体：sub == client_id。</summary>
    private static ClaimsPrincipal ServiceClientPrincipal(string clientId = "svc-a", params Claim[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new("sub", clientId),
            new("client_id", clientId),
        };
        claims.AddRange(extraClaims);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestBearer"));
    }

    /// <summary>普通用户 token 主体：sub 是用户 Id，与 client_id 不同。</summary>
    private static ClaimsPrincipal UserTokenPrincipal() =>
        new(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("client_id", "web-app")],
            "TestBearer"));

    private static void AddUserHeaders(HttpContext context)
    {
        context.Request.Headers[ServiceClientHeaders.UserId] = UserId.ToString();
        context.Request.Headers[ServiceClientHeaders.UserName] = Uri.EscapeDataString("张三");
    }

    [Fact]
    public async Task 受信服务调用_恢复用户为主身份并保留client身份()
    {
        var context = await RunAsync(ServiceClientPrincipal(), AddUserHeaders);

        Assert.Equal(UserId.ToString(), context.User.FindFirst("sub")?.Value);
        Assert.Equal("张三", context.User.FindFirst("preferred_username")?.Value);
        Assert.Equal("svc-a", context.User.FindFirst("client_id")?.Value);
        Assert.Equal("ServiceUserContext", context.User.Identity?.AuthenticationType);
        Assert.Equal(2, context.User.Identities.Count()); // 用户身份 + client 身份
    }

    [Fact]
    public async Task 受信服务调用_无用户头_保持client主体不变()
    {
        var context = await RunAsync(ServiceClientPrincipal());

        Assert.Equal("svc-a", context.User.FindFirst("sub")?.Value);
        Assert.Single(context.User.Identities);
    }

    [Fact]
    public async Task 普通用户token携带用户头_不受信且剥离头()
    {
        var context = await RunAsync(UserTokenPrincipal(), AddUserHeaders);

        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserName));
        Assert.NotEqual(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 匿名请求携带用户头_剥离头()
    {
        var context = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity()), AddUserHeaders);

        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
    }

    [Fact]
    public async Task 关闭剥离_不受信时保留头但不恢复用户()
    {
        var context = await RunAsync(
            UserTokenPrincipal(), AddUserHeaders,
            options => options.RemoveUntrustedHeaders = false);

        Assert.True(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.NotEqual(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 要求scope_标准scope形态命中()
    {
        var context = await RunAsync(
            ServiceClientPrincipal("svc-a", new Claim("scope", "other svc.call")),
            AddUserHeaders,
            options => options.RequiredScope = "svc.call");

        Assert.Equal(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 要求scope_OpenIddict形态命中()
    {
        var context = await RunAsync(
            ServiceClientPrincipal("svc-a", new Claim("oi_scp", "svc.call")),
            AddUserHeaders,
            options => options.RequiredScope = "svc.call");

        Assert.Equal(UserId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 要求scope_缺失时不受信()
    {
        var context = await RunAsync(
            ServiceClientPrincipal(),
            AddUserHeaders,
            options => options.RequiredScope = "svc.call");

        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.Equal("svc-a", context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 自定义头Claim映射_按配置恢复()
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
    public async Task 整体关闭_不恢复也不剥离()
    {
        var context = await RunAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            AddUserHeaders,
            options => options.Enable = false);

        Assert.True(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
    }
}
