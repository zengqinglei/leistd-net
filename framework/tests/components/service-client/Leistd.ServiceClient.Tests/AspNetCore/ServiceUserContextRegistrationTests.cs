using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore;
using Leistd.ServiceClient.AspNetCore.Claims;
using Leistd.ServiceClient.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.ServiceClient.Tests.AspNetCore;

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

    /// <summary>仅实现 IAsyncDisposable 的宿主转换器（原生 DI 对其同步释放同样抛错）。</summary>
    private sealed class AsyncDisposableClaimsTransformation(DisposeTracker tracker)
        : IClaimsTransformation, IAsyncDisposable
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) => Task.FromResult(principal);

        public ValueTask DisposeAsync()
        {
            tracker.Disposed = true;
            return ValueTask.CompletedTask;
        }
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
    public async Task Host_scoped_transformation_is_composed_without_becoming_singleton()
    {
        var services = CreateServices();
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value); // 宿主转换未被吞掉
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);     // 用户上下文已恢复
    }

    [Fact]
    public async Task Host_transient_factory_registration_is_composed()
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
    public async Task Host_singleton_instance_registration_is_composed()
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
    public async Task Restoration_works_without_a_host_transformation()
    {
        var services = CreateServices();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Container_created_host_transformation_is_disposed_with_the_scope()
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
    public async Task Host_supplied_instance_is_not_disposed_by_the_composition()
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
    public async Task Async_disposable_host_transformation_is_disposed_by_an_async_scope()
    {
        var tracker = new DisposeTracker();
        var services = CreateServices();
        services.AddSingleton(tracker);
        services.AddScoped<IClaimsTransformation, AsyncDisposableClaimsTransformation>();
        services.AddServiceUserContext();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
        }

        Assert.True(tracker.Disposed);
    }

    [Fact]
    public void Async_disposable_host_transformation_throws_on_sync_scope_dispose()
    {
        // 与原生 DI 行为一致：async-only 服务被同步释放要显式失败，否则泄漏被藏起来。
        var tracker = new DisposeTracker();
        var services = CreateServices();
        services.AddSingleton(tracker);
        services.AddScoped<IClaimsTransformation, AsyncDisposableClaimsTransformation>();
        services.AddServiceUserContext();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();

        var exception = Assert.Throws<InvalidOperationException>(scope.Dispose);

        Assert.Contains("IAsyncDisposable", exception.Message);
        Assert.False(tracker.Disposed);
    }

    [Fact]
    public async Task Only_the_default_registration_is_composed_and_keyed_ones_still_resolve()
    {
        // keyed 与默认服务是独立注册空间：误把 keyed 描述符当宿主转换会破坏默认服务解析。
        var counter = new CallCounter();
        var services = CreateServices(counter);
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
        services.AddKeyedSingleton<IClaimsTransformation>("external", (provider, _) =>
            new TenantClaimsTransformation(
                new RequestScopedTenantSource(),
                provider.GetRequiredService<CallCounter>()));
        services.AddServiceUserContext();

        var context = new DefaultHttpContext();
        context.Request.Headers[ServiceClientHeaders.UserId] = UserId.ToString();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>()
            .TransformAsync(ServiceClientPrincipal());

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value);   // 默认注册被组合
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
        Assert.NotNull(scope.ServiceProvider.GetKeyedService<IClaimsTransformation>("external")); // keyed 未被动过
    }

    [Fact]
    public async Task Preregistered_restoring_transformation_type_is_still_composed()
    {
        // 该公共类型是文档鼓励宿主注入的（自行组合场景），不能用它的存在推断扩展方法已执行——
        // 误判会跳过组合注册，认证阶段恢复缺失，只剩中间件一条路。
        var services = CreateServices();
        services.AddSingleton<ServiceUserContextClaimsTransformation>();
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value);
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Scoped_preregistration_of_the_restoring_transformation_still_resolves_as_singleton()
    {
        // 模板正是这条路径：AddServiceUserContext 在 AddAuthentication 之前调用，
        // 此时还没有默认 IClaimsTransformation，走的是 Singleton 别名分支。
        // 若沿用宿主的 Scoped 注册，别名就会从根容器解析 Scoped 服务而抛异常。
        var services = CreateServices();
        services.AddScoped<ServiceUserContextClaimsTransformation>();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Keyed_preregistration_of_the_restoring_transformation_is_still_composed()
    {
        var services = CreateServices();
        services.AddKeyedSingleton<ServiceUserContextClaimsTransformation>("external");
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
        services.AddServiceUserContext();

        var result = await TransformInScopeAsync(services);

        Assert.Equal("tenant-a", result.FindFirst(TenantClaimType)?.Value);
        Assert.Equal(UserId.ToString(), result.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task Repeated_registration_runs_the_host_transformation_once()
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
