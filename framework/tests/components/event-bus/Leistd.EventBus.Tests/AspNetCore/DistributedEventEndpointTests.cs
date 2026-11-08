using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text;
using Leistd.EventBus.AspNetCore.Endpoints;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.Distributed;
using Leistd.EventBus.Tests.TestDoubles;
using Leistd.ExceptionHandling.AspNetCore;
using Leistd.MultiTenancy.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.EventBus.Tests.AspNetCore;

/// <summary>D12：真实端点、授权、管理用例、SQLite 存储与统一异常处理器。</summary>
public sealed class DistributedEventEndpointTests : IAsyncLifetime
{
    private DistributedEventTestHost _backend = null!;
    private IHost _host = null!;
    private HttpClient _client = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _backend = await DistributedEventTestHost.CreateAsync();
        _host = await CreateHostAsync(true);
        _client = _host.GetTestClient();
    }

    private Task<IHost> CreateHostAsync(bool payloadEnabled) => new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddDistributedEventBus(o => { o.Outbox.DispatchEnabled = false; o.Inbox.ReceiveEnabled = false; o.Inbox.ProcessingEnabled = false; });
                services.AddGlobalExceptionHandler();
                services.AddScoped(_ => new BackendScope(_backend.Services.CreateAsyncScope()));
                services.AddScoped(sp => sp.GetRequiredService<BackendScope>().Scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>());
                services.AddScoped(sp => sp.GetRequiredService<BackendScope>().Scope.ServiceProvider.GetRequiredService<IDistributedEventManagementStore>());
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, PermissionHandler>("Test", _ => { });
                services.AddAuthorizationBuilder()
                    .AddPolicy("events.read", p => p.RequireClaim("permission", "read", "manage"))
                    .AddPolicy("events.manage", p => p.RequireClaim("permission", "manage"));
            })
            .Configure(app =>
            {
                app.UseGlobalExceptionHandler();
                app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
                app.Use(async (context, next) =>
                {
                    var backend = context.RequestServices.GetRequiredService<BackendScope>().Scope.ServiceProvider;
                    var id = Guid.TryParse(context.Request.Headers["X-Tenant"].ToString(), out var tenantId) ? tenantId : (Guid?)null;
                    using var tenant = backend.GetRequiredService<ICurrentTenant>().Change(id);
                    await next();
                });
                app.UseEndpoints(endpoints => endpoints.MapGroup("/api/v1/distributed-events").MapDistributedEvents(o =>
                { o.ReadPolicy = "events.read"; o.ManagePolicy = "events.manage"; o.EnablePayloadEndpoints = payloadEnabled; }));
            })).StartAsync();

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client.Dispose(); await _host.StopAsync(); _host.Dispose(); await _backend.DisposeAsync();
    }

    /// <summary>只有 Read 时，全部载荷与写入口返回 403，不能绕过 Manage 策略。</summary>
    [Theory]
    [InlineData("GET", "outbox/1/payload")]
    [InlineData("GET", "inbox/1/payload")]
    [InlineData("GET", "rejections/1/payload")]
    [InlineData("POST", "outbox/redeliver")]
    [InlineData("POST", "inbox/replay")]
    [InlineData("POST", "rejections/redecode")]
    [InlineData("POST", "rejections/discard")]
    [InlineData("POST", "test")]
    public async Task Read_only_callers_cannot_read_payloads_or_write(string method, string route)
    {
        var response = await SendAsync(method, route, "read");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>租户不能选择物理库、访问拒收或数据库目录；稳定错误码实际映射为 403。</summary>
    [Theory]
    [InlineData("status?database=host")]
    [InlineData("outbox?database=host")]
    [InlineData("inbox?database=host")]
    [InlineData("catalog?database=host")]
    [InlineData("rejections")]
    [InlineData("rejections/1")]
    [InlineData("databases")]
    public async Task Tenant_host_only_operations_return_403_with_the_component_code(string route)
    {
        var response = await SendAsync("GET", route, "read", DistributedEventTestHost.TenantA);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DistributedEvents:HostOnly", json.RootElement.GetProperty("code").GetString());
    }

    /// <summary>缺省分页可调用；列表与详情概要不泄漏载荷，ID 序列化为十进制字符串。</summary>
    [Fact]
    public async Task Read_lists_use_default_paging_and_expose_string_ids_without_payload()
    {
        var sent = await SendAsync("POST", "test", "manage");
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var page = await SendAsync("GET", "outbox", "read");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var text = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain("payload", text, StringComparison.OrdinalIgnoreCase);
        var id = JsonDocument.Parse(text).RootElement.GetProperty("items")[0].GetProperty("id");
        Assert.Equal(JsonValueKind.String, id.ValueKind);
        var detail = await SendAsync("GET", "outbox/" + id.GetString(), "read");
        Assert.DoesNotContain("payload", await detail.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync("GET", "status", null)).StatusCode);
    }

    /// <summary>关闭载荷能力是移除路由，持有 Manage 的调用方同样只能得到 404。</summary>
    [Theory]
    [InlineData("outbox/1/payload", DistributedEventEndpoints.GetOutboxPayloadName)]
    [InlineData("inbox/1/payload", DistributedEventEndpoints.GetInboxPayloadName)]
    [InlineData("rejections/1/payload", DistributedEventEndpoints.GetRejectionPayloadName)]
    public async Task Disabled_payload_routes_are_not_mapped_even_for_managers(string route, string endpointName)
    {
        using var host = await CreateHostAsync(false);
        using var client = host.GetTestClient();
        Assert.DoesNotContain(host.Services.GetRequiredService<EndpointDataSource>().Endpoints,
            endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/distributed-events/" + route);
        request.Headers.Add("X-Permission", "manage");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
        await host.StopAsync();
    }

    /// <summary>端点快照报告实际映射的载荷能力，管理服务本身不决定 HTTP 能力。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Status_reports_the_mapping_capability_without_coupling_the_management_service(bool enabled)
    {
        using var host = await CreateHostAsync(enabled);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/distributed-events/status");
        request.Headers.Add("X-Permission", "read");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(enabled, json.RootElement.GetProperty("payloadEnabled").GetBoolean());
        await using var scope = _backend.Services.CreateAsyncScope();
        var direct = await scope.ServiceProvider.GetRequiredService<IDistributedEventManagementService>().GetStatusAsync();
        Assert.Null(direct.PayloadEnabled);
        await host.StopAsync();
    }

    private Task<HttpResponseMessage> SendAsync(string method, string route, string? permission, Guid? tenant = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/distributed-events/" + route);
        if (permission is not null) request.Headers.Add("X-Permission", permission);
        if (tenant is not null) request.Headers.Add("X-Tenant", tenant.ToString());
        if (method == "POST") request.Content = new StringContent("{\"ids\":[\"1\"]}", Encoding.UTF8, "application/json");
        return _client.SendAsync(request);
    }

    private sealed class BackendScope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public AsyncServiceScope Scope { get; } = scope;
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }
    private sealed class PermissionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Permission", out var permission)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", permission.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
