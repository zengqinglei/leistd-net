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
/// 不吞掉宿主已注册的转换（组合：宿主在前、恢复在后）、沿用其生命周期、重复调用不叠加。
/// </summary>
public class ServiceUserContextRegistrationTests
{
    private const string TenantClaimType = "tenant";
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>请求级依赖：Scoped 宿主转换器的典型形态。</summary>
    private sealed class RequestScopedTenantSource
    {
        public string TenantId { get; } = "tenant-a";
    }

    /// <summary>宿主的 claims 富化：给主体补一个租户 claim，并计数调用次数。</summary>
    private sealed class TenantClaimsTransformation(RequestScopedTenantSource source, CallCounter counter)
        : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            counter.Increment();
            if (principal.FindFirst(TenantClaimType) is null &&
                principal.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim(TenantClaimType, source.TenantId));
            }

            return Task.FromResult(principal);
        }
    }

    /// <summary>持有需释放资源的宿主转换器（释放责任随组合转移的验证对象）。</summary>
    private sealed class DisposableClaimsTransformation(DisposeTracker tracker) : IClaimsTransformation, IDisposable
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) => Task.FromResult(principal);

        public void Dispose() => tracker.Disposed = true;
    }

    private sealed class DisposeTracker
    {
        public bool Disposed { get; set; }
    }

    private sealed class CallCounter
    {
        private int _count;
        public int Count => _count;
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private static ServiceCollection CreateServices(CallCounter? counter = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(counter ?? new CallCounter());
        services.AddScoped<RequestScopedTenantSource>();
        return services;
    }

    /// <summary>按真实宿主的方式构建容器（开启作用域校验），并在请求作用域内执行转换。</summary>
    private static async Task<ClaimsPrincipal> TransformInScopeAsync(
        IServiceCollection services, ClaimsPrincipal? principal = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ServiceClientHeaders.UserId] = UserId.ToString();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });

        // ValidateScopes/ValidateOnBuild：把「scoped 依赖被提升为单例」变成构建期/解析期错误，
        // 而不是运行期的跨请求状态污染。
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        await using var scope = provider.CreateAsyncScope();
        var transformation = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
        return await transformation.TransformAsync(principal ?? ServiceClientPrincipal());
    }

    private static ClaimsPrincipal ServiceClientPrincipal(string clientId = "svc-a") =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", ClientSubject.Format(clientId)),
                new Claim("client_id", clientId),
                new Claim("scope", ServiceClientScopes.Delegation),
            ],
            "TestBearer"));

    [Fact]
    public async Task 宿主Scoped转换_两者都生效且不被提升为单例()
    {
        var services = CreateServices();
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value); // 宿主转换未被吞掉
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);     // 用户上下文已恢复
    }

    [Fact]
    public async Task 宿主Transient工厂注册_同样被组合()
    {
        var services = CreateServices();
        services.AddTransient<IClaimsTransformation>(provider => new TenantClaimsTransformation(
            provider.GetRequiredService<RequestScopedTenantSource>(),
            provider.GetRequiredService<CallCounter>()));
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value);
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 宿主单例实例注册_同样被组合()
    {
        var counter = new CallCounter();
        var services = CreateServices(counter);
        services.AddSingleton<IClaimsTransformation>(
            new TenantClaimsTransformation(new RequestScopedTenantSource(), counter));
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value);
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 宿主未注册转换_恢复照常生效()
    {
        var services = CreateServices();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 宿主转换由容器创建_作用域结束时被释放()
    {
        // 内层不再由 DI 跟踪（组合在自己的工厂里创建它），释放责任必须随所有权转移到组合，
        // 否则 Scoped 宿主转换器每请求泄漏一个未释放实例。
        var tracker = new DisposeTracker();
        var services = CreateServices();
        services.AddSingleton(tracker);
        services.AddScoped<IClaimsTransformation, DisposableClaimsTransformation>();
        services.AddServiceUserContext();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
            Assert.False(tracker.Disposed);
        }

        Assert.True(tracker.Disposed);
    }

    [Fact]
    public async Task 宿主自行new的实例_不被组合释放()
    {
        // ImplementationInstance 由宿主创建，容器本就不拥有它；组合越权释放会把宿主
        // 仍在使用的对象提前销毁。
        var tracker = new DisposeTracker();
        var services = CreateServices();
        services.AddSingleton<IClaimsTransformation>(new DisposableClaimsTransformation(tracker));
        services.AddServiceUserContext();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        provider.GetRequiredService<IClaimsTransformation>();
        await provider.DisposeAsync();

        Assert.False(tracker.Disposed);
    }

    [Fact]
    public async Task 重复注册_宿主转换只跑一次_不叠加嵌套()
    {
        var counter = new CallCounter();
        var services = CreateServices(counter);
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
        services.AddServiceUserContext();
        services.AddServiceUserContext();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal(1, counter.Count); // 每多包一层，宿主转换就会多跑一次
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
        Assert.Single(result.Identities, identity => identity.AuthenticationType == "ServiceUserContext");
    }
}
