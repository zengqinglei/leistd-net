using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore;
using Leistd.ServiceClient.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.ServiceClient.Tests;

/// <summary>
/// <c>AddServiceUserContext</c> 的 <see cref="IClaimsTransformation"/> 注册契约：
/// 不吞掉宿主已注册的转换，而是组合（宿主在前、用户上下文恢复在后）。
/// </summary>
public class ServiceUserContextRegistrationTests
{
    private const string TenantClaimType = "tenant";
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>宿主的 claims 富化：给主体补一个租户 claim。</summary>
    private sealed class TenantClaimsTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.FindFirst(TenantClaimType) is null &&
                principal.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim(TenantClaimType, "tenant-a"));
            }

            return Task.FromResult(principal);
        }
    }

    private static async Task<ClaimsPrincipal> TransformAsync(IServiceCollection services)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ServiceClientHeaders.UserId] = UserId.ToString();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });

        var provider = services.BuildServiceProvider();
        var transformation = provider.GetRequiredService<IClaimsTransformation>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", ClientSubject.Format("svc-a")), new Claim("client_id", "svc-a")],
            "TestBearer"));

        return await transformation.TransformAsync(principal);
    }

    [Fact]
    public async Task 宿主已注册转换_两者都生效且宿主在前()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClaimsTransformation, TenantClaimsTransformation>();
        services.AddServiceUserContext();

        var result = await TransformAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value);      // 宿主转换未被吞掉
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);          // 用户上下文已恢复
    }

    [Fact]
    public async Task 宿主用实例注册_同样被组合()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClaimsTransformation>(new TenantClaimsTransformation());
        services.AddServiceUserContext();

        var result = await TransformAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value);
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 宿主未注册转换_恢复照常生效()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceUserContext();

        var result = await TransformAsync(services);

        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 重复注册_不叠加也不失效()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceUserContext();
        services.AddServiceUserContext();

        var result = await TransformAsync(services);

        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
        Assert.Single(result.Identities, identity => identity.AuthenticationType == "ServiceUserContext");
    }
}
