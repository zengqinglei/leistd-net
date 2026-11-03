using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Leistd.ExceptionHandling.AspNetCore;
using Leistd.Webhooks.AspNetCore.Endpoints;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Errors;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Stores;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Webhooks.Tests.AspNetCore;

/// <summary>
/// 管理端点：两个策略必填、写端点只认管理策略（A14 只读权限调写端点 403）、匿名 401；
/// 组件错误码的默认状态（404 / 409）、删除 204、输入校验 400，<c>retryFailed</c> 走查询串。
/// </summary>
/// <remarks>端点经真实的管理用例、EF 存储与全局异常处理器；漏登记状态映射时 404 / 409 会静默回落成 400。</remarks>
public sealed class WebhookEndpointTests : IAsyncLifetime
{
    private const string ReadPolicy = "webhooks.read";
    private const string ManagePolicy = "webhooks.manage";

    private WebhookTestHost _backend = null!;
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _backend = await WebhookTestHost.CreateAsync();
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddWebhooks();
                    services.AddGlobalExceptionHandler();
                    // 每个请求一个后端作用域，管理用例取自装好 EF 存储的测试宿主
                    services.AddScoped(_ => new BackendScope(_backend.Services.CreateAsyncScope()));
                    services.AddScoped(sp => sp.GetRequiredService<BackendScope>().Scope.ServiceProvider.GetRequiredService<IWebhookManagementService>());
                    // 存储也取自后端：映射时据此确认存储已注册
                    services.AddScoped(sp => sp.GetRequiredService<BackendScope>().Scope.ServiceProvider.GetRequiredService<IWebhookManagementStore>());
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, PermissionHandler>("Test", _ => { });
                    services.AddAuthorizationBuilder()
                        .AddPolicy(ReadPolicy, policy => policy.RequireClaim("permission", "read", "manage"))
                        .AddPolicy(ManagePolicy, policy => policy.RequireClaim("permission", "manage"));
                })
                .Configure(app =>
                {
                    app.UseGlobalExceptionHandler();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGroup("/api/v1/webhooks").MapWebhooks(options =>
                    {
                        options.ReadPolicy = ReadPolicy;
                        options.ManagePolicy = ManagePolicy;
                    }));
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _backend.DisposeAsync();
    }

    /// <summary>漏配任何一个策略名，映射时即抛出，而不是留一组谁都能调的端点。</summary>
    [Theory]
    [InlineData(null, ManagePolicy, nameof(WebhookEndpointOptions.ReadPolicy))]
    [InlineData(ReadPolicy, null, nameof(WebhookEndpointOptions.ManagePolicy))]
    public async Task Mapping_without_either_policy_fails(string? read, string? manage, string missing)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped(_ => (IWebhookManagementService)null!);
        await using var app = builder.Build();

        var error = Assert.Throws<ArgumentException>(() => app.MapWebhooks(options =>
        {
            options.ReadPolicy = read ?? string.Empty;
            options.ManagePolicy = manage ?? string.Empty;
        }));

        Assert.Equal(missing, error.ParamName);
    }

    /// <summary>没有注册存储（管理用例）时映射即抛出，而不是第一次请求才 500。</summary>
    [Fact]
    public async Task Mapping_without_a_webhook_store_fails()
    {
        await using var app = WebApplication.CreateBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.MapWebhooks(options =>
        {
            options.ReadPolicy = ReadPolicy;
            options.ManagePolicy = ManagePolicy;
        }));

        Assert.Contains("AddWebhooksEfCore", error.Message);
    }

    /// <summary>管理用例随 Core 注册，但读写要经存储：只调了 AddWebhooks 时用例已在容器里，映射仍因缺存储而抛出。</summary>
    [Fact]
    public async Task Mapping_with_only_the_core_registration_fails()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddWebhooks();
        await using var app = builder.Build();

        Assert.True(app.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IWebhookManagementService)));
        var error = Assert.Throws<InvalidOperationException>(() => app.MapWebhooks(options =>
        {
            options.ReadPolicy = ReadPolicy;
            options.ManagePolicy = ManagePolicy;
        }));

        Assert.Contains("AddWebhooksEfCore", error.Message);
    }

    /// <summary>A14：只有只读权限时，每个写端点都是 403，且什么都没写。</summary>
    [Theory]
    [InlineData("POST", "subscriptions")]
    [InlineData("PUT", "subscriptions/{0}")]
    [InlineData("DELETE", "subscriptions/{0}")]
    [InlineData("POST", "subscriptions/{0}/enable")]
    [InlineData("POST", "subscriptions/{0}/disable")]
    [InlineData("POST", "subscriptions/{0}/rotate-secret")]
    [InlineData("POST", "subscriptions/{0}/test")]
    [InlineData("POST", "deliveries/{0}/retry")]
    public async Task A_read_only_caller_is_forbidden_from_every_write_endpoint(string method, string route)
    {
        var response = await SendAsync(method, string.Format(route, Guid.NewGuid()), "read", ValidBody);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, (await _backend.ManageAsync(null, service => service.GetSubscriptionsAsync(new()))).TotalCount);
    }

    [Theory]
    [InlineData("event-types", null, HttpStatusCode.Unauthorized)]
    [InlineData("subscriptions", null, HttpStatusCode.Unauthorized)]
    [InlineData("deliveries", null, HttpStatusCode.Unauthorized)]
    [InlineData("event-types", "read", HttpStatusCode.OK)]
    [InlineData("subscriptions", "read", HttpStatusCode.OK)]
    [InlineData("deliveries", "read", HttpStatusCode.OK)]
    public async Task Read_endpoints_require_the_read_policy(string route, string? permission, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await SendAsync("GET", route, permission)).StatusCode);
    }

    /// <summary>找不到的订阅与投递是带码的 404。</summary>
    [Theory]
    [InlineData("GET", "subscriptions/{0}", WebhookErrorCodes.SubscriptionNotFound)]
    [InlineData("POST", "subscriptions/{0}/disable", WebhookErrorCodes.SubscriptionNotFound)]
    [InlineData("DELETE", "subscriptions/{0}", WebhookErrorCodes.SubscriptionNotFound)]
    [InlineData("GET", "deliveries/{0}", WebhookErrorCodes.DeliveryNotFound)]
    [InlineData("POST", "deliveries/{0}/retry", WebhookErrorCodes.DeliveryNotFound)]
    public async Task Missing_resources_are_404_with_the_component_code(string method, string route, string code)
    {
        var response = await SendAsync(method, string.Format(route, Guid.NewGuid()), "manage");

        await AssertCodeAsync(response, HttpStatusCode.NotFound, code);
    }

    /// <summary>停用订阅上发测试事件、重试中的投递手动重发，都是带码的 409。</summary>
    [Fact]
    public async Task State_conflicts_are_409_with_the_component_code()
    {
        var pendingId = await CreateWithPendingDeliveryAsync();
        var disabled = await CreateViaApiAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("POST", $"subscriptions/{disabled}/disable", "manage")).StatusCode);

        await AssertCodeAsync(await SendAsync("POST", $"subscriptions/{disabled}/test", "manage"), HttpStatusCode.Conflict, WebhookErrorCodes.SubscriptionDisabled);
        await AssertCodeAsync(await SendAsync("POST", $"deliveries/{pendingId}/retry", "manage"), HttpStatusCode.Conflict, WebhookErrorCodes.DeliveryNotRetryable);
    }

    [Fact]
    public async Task Deleting_a_subscription_returns_204()
    {
        var id = await CreateViaApiAsync();

        var response = await SendAsync("DELETE", $"subscriptions/{id}", "manage");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, (await _backend.ManageAsync(null, service => service.GetSubscriptionsAsync(new()))).TotalCount);
    }

    /// <summary>入参校验失败是 400，字段名按 JSON 命名，不带业务码。</summary>
    [Theory]
    [InlineData("POST", "subscriptions", "{}", "name")]
    [InlineData("POST", "subscriptions", """{"name":"a","url":"https://r.example","eventTypes":["*","order.paid"]}""", "eventTypes")]
    [InlineData("POST", "subscriptions", """{"name":"a","url":"mailto:a@r.example","eventTypes":["order.paid"]}""", "url")]
    [InlineData("POST", "subscriptions", """{"name":"a","url":"https://r.example","eventTypes":["order.paid"],"headers":{"webhook-id":"x"}}""", "headers")]
    [InlineData("GET", "deliveries?status=Failed", null, "status")]
    [InlineData("GET", "subscriptions?limit=0", null, "limit")]
    public async Task Invalid_input_is_400_with_the_field(string method, string route, string? body, string field)
    {
        var response = await SendAsync(method, route, "manage", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.TryGetProperty("code", out _));
        Assert.Contains(field, document.RootElement.GetProperty("errors").EnumerateArray().Select(x => x.GetProperty("field").GetString()));
    }

    /// <summary>与前端约定：<c>retryFailed=true</c> 走查询串，失败投递按手动重发排期。</summary>
    [Fact]
    public async Task Enable_takes_retryFailed_from_the_query_string()
    {
        var id = await CreateViaApiAsync();
        await _backend.PublishAsync(null);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("POST", $"subscriptions/{id}/disable", "manage")).StatusCode);

        var response = await SendAsync("POST", $"subscriptions/{id}/enable?retryFailed=true", "manage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var delivery = Assert.Single(await _backend.DeliveriesAsync());
        Assert.Equal((WebhookDeliveryStatus.Pending, WebhookAttemptTrigger.Manual), (delivery.Status, delivery.NextTrigger));
    }

    private const string ValidBody = """{"name":"a","url":"https://r.example/hook","eventTypes":["order.paid"]}""";

    private async Task<Guid> CreateViaApiAsync()
    {
        var response = await SendAsync("POST", "subscriptions", "manage", ValidBody);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("whsec_", document.RootElement.GetProperty("secret").GetString());
        return document.RootElement.GetProperty("subscription").GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateWithPendingDeliveryAsync()
    {
        var id = await CreateViaApiAsync();
        var response = await SendAsync("POST", $"subscriptions/{id}/test", "manage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> SendAsync(string method, string route, string? permission, string? body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/webhooks/" + route);
        if (permission is not null)
        {
            request.Headers.Add("X-Permission", permission);
        }

        if (body is not null || method is "POST" or "PUT")
        {
            request.Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json");
        }

        return _client.SendAsync(request);
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    private sealed class BackendScope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public AsyncServiceScope Scope { get; } = scope;

        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    // X-Permission 头给出 permission 声明；没有该头即匿名
    private sealed class PermissionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Permission", out var permission))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("permission", permission.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
