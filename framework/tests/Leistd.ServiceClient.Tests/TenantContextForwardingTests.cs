using System.Security.Claims;
using Leistd.MultiTenancy;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Middlewares;
using Leistd.ServiceClient.AspNetCore.Options;
using Leistd.ServiceClient.Constants;
using Leistd.ServiceClient.Handlers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Leistd.ServiceClient.Tests;

/// <summary>
/// 租户上下文的跨服务传递：出站 X-Tenant-Id 注入与被调方受信恢复。
/// </summary>
public class TenantContextForwardingTests
{
    private sealed class FakeCurrentTenant(Guid? id) : ICurrentTenant
    {
        public bool IsAvailable => Id.HasValue;
        public Guid? Id { get; } = id;
        public string? Name => null;
        public IDisposable Change(Guid? id, string? name = null) => throw new NotSupportedException();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> SendAsync(Guid? tenantId, Action<HttpRequestMessage>? configure = null)
    {
        var capturing = new CapturingHandler();
        var handler = new TenantContextDelegatingHandler(new FakeCurrentTenant(tenantId))
        {
            InnerHandler = capturing
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://downstream/api/x");
        configure?.Invoke(request);
        await invoker.SendAsync(request, CancellationToken.None);
        return capturing.Request!;
    }

    [Fact]
    public async Task 租户上下文存在_注入租户头()
    {
        var tenantId = Guid.NewGuid();
        var request = await SendAsync(tenantId);

        Assert.Equal(tenantId.ToString(), request.Headers.GetValues(ServiceClientHeaders.TenantId).Single());
    }

    [Fact]
    public async Task 宿主上下文_不注入租户头()
    {
        var request = await SendAsync(tenantId: null);

        Assert.False(request.Headers.Contains(ServiceClientHeaders.TenantId));
    }

    [Fact]
    public async Task 请求已有同名头_不覆盖()
    {
        var preset = Guid.NewGuid().ToString();
        var request = await SendAsync(
            Guid.NewGuid(),
            r => r.Headers.TryAddWithoutValidation(ServiceClientHeaders.TenantId, preset));

        Assert.Equal(preset, request.Headers.GetValues(ServiceClientHeaders.TenantId).Single());
    }

    // ---- 被调方恢复 ----

    private static async Task<HttpContext> RunMiddlewareAsync(ClaimsPrincipal user, Action<HttpContext>? configureRequest = null)
    {
        var middleware = new ServiceUserContextMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Options.Options.Create(new ServiceUserContextOptions()),
            NullLogger<ServiceUserContextMiddleware>.Instance);

        var context = new DefaultHttpContext { User = user };
        configureRequest?.Invoke(context);
        await middleware.InvokeAsync(context);
        return context;
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
    public async Task 机器主体契约_受信恢复()
    {
        var userId = Guid.NewGuid();
        var context = await RunMiddlewareAsync(
            ServiceClientPrincipal(),
            ctx => ctx.Request.Headers[ServiceClientHeaders.UserId] = userId.ToString());

        Assert.Equal(userId.ToString(), context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task 受信服务调用_仅租户头无用户头_恢复租户claim()
    {
        var tenantId = Guid.NewGuid();
        var context = await RunMiddlewareAsync(
            ServiceClientPrincipal(),
            ctx => ctx.Request.Headers[ServiceClientHeaders.TenantId] = tenantId.ToString());

        // 后台任务场景：只有租户上下文没有用户，也必须恢复出 tenant_id claim
        Assert.Equal(tenantId.ToString(), context.User.FindFirst(CustomClaimTypes.TenantId)?.Value);
        Assert.Equal("svc-a", context.User.FindFirst("client_id")?.Value);
    }

    [Fact]
    public async Task 受信服务调用_用户头与租户头同时恢复()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var context = await RunMiddlewareAsync(
            ServiceClientPrincipal(),
            ctx =>
            {
                ctx.Request.Headers[ServiceClientHeaders.UserId] = userId.ToString();
                ctx.Request.Headers[ServiceClientHeaders.TenantId] = tenantId.ToString();
            });

        Assert.Equal(userId.ToString(), context.User.FindFirst("sub")?.Value);
        Assert.Equal(tenantId.ToString(), context.User.FindFirst(CustomClaimTypes.TenantId)?.Value);
    }

    [Fact]
    public async Task 不受信来源_不恢复claim_但保留租户头()
    {
        var tenantId = Guid.NewGuid();
        var context = await RunMiddlewareAsync(
            new ClaimsPrincipal(new ClaimsIdentity()), // 匿名
            ctx =>
            {
                ctx.Request.Headers[ServiceClientHeaders.UserId] = Guid.NewGuid().ToString();
                ctx.Request.Headers[ServiceClientHeaders.TenantId] = tenantId.ToString();
            });

        // 用户头剥离；租户头保留——匿名登录的租户选择依赖它，
        // 且解析链的主体优先级已使其无法改写已认证会话的租户
        Assert.Null(context.User.FindFirst(CustomClaimTypes.TenantId));
        Assert.False(context.Request.Headers.ContainsKey(ServiceClientHeaders.UserId));
        Assert.Equal(tenantId.ToString(), context.Request.Headers[ServiceClientHeaders.TenantId]);
    }
}
