using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Claims;
using Leistd.ServiceClient.AspNetCore.Options;
using Leistd.ServiceClient.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Leistd.ServiceClient.Tests;

public class ServiceUserContextClaimsTransformationTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<ClaimsPrincipal> TransformAsync(
        ClaimsPrincipal principal,
        Action<IHeaderDictionary>? configureHeaders = null,
        Action<ServiceUserContextOptions>? configureOptions = null,
        bool withHttpContext = true)
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
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<ServiceUserContextClaimsTransformation>.Instance);
        return await transformation.TransformAsync(principal);
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
        headers[ServiceClientHeaders.UserName] = Uri.EscapeDataString("张三");
    }

    [Fact]
    public async Task 受信服务主体_认证阶段即恢复用户为主身份()
    {
        var result = await TransformAsync(ServiceClientPrincipal(), AddUserHeaders);

        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
        Assert.Equal("张三", result.FindFirst("preferred_username")?.Value);
        Assert.Equal("svc-a", result.FindFirst("client_id")?.Value);
        Assert.Equal(2, result.Identities.Count());
    }

    [Fact]
    public async Task 已恢复过的主体_幂等原样返回()
    {
        var first = await TransformAsync(ServiceClientPrincipal(), AddUserHeaders);

        var second = await TransformAsync(first, AddUserHeaders);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task 非服务主体_不做任何处理()
    {
        var userToken = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("client_id", "web-app")],
            "TestBearer"));

        var result = await TransformAsync(userToken, AddUserHeaders);

        Assert.Same(userToken, result);
    }

    [Fact]
    public async Task 无HTTP上下文_原样返回()
    {
        var principal = ServiceClientPrincipal();

        var result = await TransformAsync(principal, withHttpContext: false);

        Assert.Same(principal, result);
    }

    [Fact]
    public async Task 无用户头_原样返回()
    {
        var principal = ServiceClientPrincipal();

        var result = await TransformAsync(principal);

        Assert.Same(principal, result);
    }

    [Fact]
    public async Task 整体关闭_原样返回()
    {
        var principal = ServiceClientPrincipal();

        var result = await TransformAsync(
            principal, AddUserHeaders, options => options.Enable = false);

        Assert.Same(principal, result);
    }
}
