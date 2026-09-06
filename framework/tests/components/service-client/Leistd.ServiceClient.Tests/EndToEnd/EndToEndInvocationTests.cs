using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Leistd.Security.AspNetCore;
using Leistd.Security.Claims;
using Leistd.Security.Users;
using Leistd.ServiceClient.AspNetCore;
using Leistd.ServiceClient.Constants;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Http;
using Leistd.ServiceClient.OAuth;
using Leistd.ServiceClient.OAuth.Services;
using Leistd.ServiceClient.Options;
using Leistd.TestBase.Doubles;
using Leistd.Tracing.AspNetCore;
using Leistd.Tracing;
using Leistd.Tracing.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Leistd.Tracing.Abstractions;

namespace Leistd.ServiceClient.Tests.EndToEnd;

/// <summary>
/// 端到端闭环：TestServer 双宿主（身份服务 + 资源服务）验证
/// token 获取/缓存/401 自愈 → Bearer 验证 → TraceId 与用户头全链路透传 →
/// 被调方 ICurrentUser/ICurrentClient 生效 → 远端错误还原。
/// </summary>
public sealed class EndToEndInvocationTests : IAsyncLifetime
{
    private const string ClientId = "svc-a";
    private const string ClientSecret = "s3cret";
    private const string RequiredScope = "svc.call";

    private WebApplication _identityHost = null!;
    private WebApplication _resourceHost = null!;
    private int _tokenRequests;
    private int _tokenSerial;
    private readonly TokenPolicy _tokenPolicy = new();

    public async Task InitializeAsync()
    {
        _identityHost = await StartIdentityHostAsync();
        _resourceHost = await StartResourceHostAsync();
    }

    public async Task DisposeAsync()
    {
        await _identityHost.DisposeAsync();
        await _resourceHost.DisposeAsync();
    }


    private async Task<WebApplication> StartIdentityHostAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        app.MapPost("/connect/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            if (form["grant_type"] != "client_credentials" ||
                form["client_id"] != ClientId ||
                form["client_secret"] != ClientSecret)
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: 400);
            }

            Interlocked.Increment(ref _tokenRequests);
            var serial = Interlocked.Increment(ref _tokenSerial);
            var token = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
            {
                client_id = ClientId,
                scope = form["scope"].ToString(),
                serial,
            }));
            return Results.Json(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
        });

        await app.StartAsync();
        return app;
    }


    private sealed class TokenPolicy
    {
        /// <summary>低于该序号的令牌视为已吊销（模拟密钥轮换/撤销）。</summary>
        public int MinSerial { get; set; } = 1;
    }

    /// <summary>按端到端形态验证 Bearer 令牌（形状与 OpenIddict 验证后的主体一致：sub/client_id/scope）。</summary>
    private sealed class TestBearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TokenPolicy policy) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestBearer";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            try
            {
                using var document = JsonDocument.Parse(Convert.FromBase64String(header["Bearer ".Length..]));
                var root = document.RootElement;
                var serial = root.GetProperty("serial").GetInt32();
                if (serial < policy.MinSerial)
                {
                    return Task.FromResult(AuthenticateResult.Fail("token revoked"));
                }

                var clientId = root.GetProperty("client_id").GetString()!;
                var identity = new ClaimsIdentity(
                    [
                        new Claim("sub", ClientSubject.Format(clientId)),
                        new Claim("client_id", clientId),
                        new Claim("scope", root.GetProperty("scope").GetString() ?? string.Empty),
                    ],
                    SchemeName);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AuthenticateResult.Fail(ex));
            }
        }
    }

    private async Task<WebApplication> StartResourceHostAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(_tokenPolicy);
        builder.Services.AddAuthentication(TestBearerAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestBearerAuthenticationHandler>(
                TestBearerAuthenticationHandler.SchemeName, _ => { });
        // 与模板形态一致：默认策略显式声明 scheme，PolicyEvaluator 会按 scheme 重认证并覆盖
        // HttpContext.User——验证用户上下文恢复发生在认证阶段（ClaimsTransformation），而非仅中间件。
        builder.Services.AddAuthorization(options => options.DefaultPolicy =
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                    TestBearerAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build());
        builder.Services.AddSecurity();
        builder.Services.AddCorrelationId(_ => { });
        builder.Services.AddServiceUserContext(options => options.RequiredScope = RequiredScope);

        var app = builder.Build();
        app.UseCorrelationId();
        app.UseAuthentication();
        app.UseServiceUserContext();
        app.UseAuthorization();

        app.MapGet("/api/whoami", (ICurrentUser currentUser, Leistd.Security.Clients.ICurrentClient currentClient,
                ICorrelationIdProvider correlationId) =>
            Results.Json(new
            {
                code = 0,
                data = new
                {
                    userId = currentUser.Id,
                    username = currentUser.Username,
                    clientId = currentClient.ClientId,
                    traceId = correlationId.Get(),
                },
            })).RequireAuthorization();

        // 匿名端点：验证伪造头被剥离后 ICurrentUser 不会被污染
        app.MapGet("/api/echo-user", (ICurrentUser currentUser) =>
            Results.Json(new { code = 0, data = new { userId = currentUser.Id } }));

        app.MapGet("/api/fail", () => Results.Json(new
        {
            type = "https://err/business",
            title = "Not Found",
            status = 404,
            code = "Order:NotFound",
            message = "订单不存在",
            traceId = "remote-trace-42",
            errors = (object?)null,
        }, statusCode: 404)).RequireAuthorization();

        await app.StartAsync();
        return app;
    }


    public sealed class DemoClientOptions : ServiceClientOptions;

    public interface IDemoClient
    {
        Task<WhoAmI?> WhoAmIAsync();

        Task<WhoAmI?> EchoUserAsync();

        Task FailAsync();
    }

    public sealed record WhoAmI(Guid? UserId, string? Username, string? ClientId, string? TraceId);

    public sealed class DemoClient(HttpClient httpClient) : IDemoClient
    {
        public async Task<WhoAmI?> WhoAmIAsync()
        {
            var response = await httpClient.GetAsync("api/whoami");
            return await response.ReadResultAsync<WhoAmI>();
        }

        public async Task<WhoAmI?> EchoUserAsync()
        {
            var response = await httpClient.GetAsync("api/echo-user");
            return await response.ReadResultAsync<WhoAmI>();
        }

        public async Task FailAsync()
        {
            var response = await httpClient.GetAsync("api/fail");
            await response.ReadResultAsync();
        }
    }

    private ServiceProvider CreateCaller(ICurrentUser? currentUser)
    {
        // 走真实配置契约：全局 Leistd:ServiceAuth（本服务调用身份，一次）+ 客户端节 Scope
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Leistd:ServiceAuth:TokenEndpoint"] = "http://identity/connect/token",
            ["Leistd:ServiceAuth:ClientId"] = ClientId,
            ["Leistd:ServiceAuth:ClientSecret"] = ClientSecret,
            ["Leistd:ServiceClients:DemoService:BaseAddress"] = "http://demo-service",
            ["Leistd:ServiceClients:DemoService:Scope"] = RequiredScope,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCorrelationIdCore(_ => { });
        if (currentUser is not null)
        {
            services.AddSingleton(currentUser);
        }

        services.AddServiceClient<IDemoClient, DemoClient, DemoClientOptions>("DemoService", configuration)
            .ConfigurePrimaryHttpMessageHandler(() => _resourceHost.GetTestServer().CreateHandler())
            .AddClientCredentials(configuration);

        services.AddHttpClient(ClientCredentialsTokenProvider.TokenHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => _identityHost.GetTestServer().CreateHandler());

        return services.BuildServiceProvider();
    }


    [Fact]
    public async Task 全链路_令牌获取_追踪与用户上下文透传_被调方双通道身份生效()
    {
        var userId = Guid.NewGuid();
        await using var caller = CreateCaller(new FakeCurrentUser(id: userId, username: "张三"));
        var client = caller.GetRequiredService<IDemoClient>();
        var correlation = caller.GetRequiredService<ICorrelationIdProvider>();

        WhoAmI? result;
        using (correlation.Change("trace-e2e-1"))
        {
            result = await client.WhoAmIAsync();
        }

        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);          // X-User-Id → 被调方 ICurrentUser.Id
        Assert.Equal("张三", result.Username);        // X-Username URL 编码往返
        Assert.Equal(ClientId, result.ClientId);      // 调用方 client 身份保留（ICurrentClient）
        Assert.Equal("trace-e2e-1", result.TraceId);  // TraceId 全链路透传
        Assert.Equal(1, _tokenRequests);
    }

    [Fact]
    public async Task 令牌缓存_连续调用只取一次令牌()
    {
        await using var caller = CreateCaller(new FakeCurrentUser(id: Guid.NewGuid()));
        var client = caller.GetRequiredService<IDemoClient>();

        await client.WhoAmIAsync();
        await client.WhoAmIAsync();

        Assert.Equal(1, _tokenRequests);
    }

    [Fact]
    public async Task 令牌被吊销_401自愈重取后调用成功()
    {
        await using var caller = CreateCaller(new FakeCurrentUser(id: Guid.NewGuid()));
        var client = caller.GetRequiredService<IDemoClient>();

        await client.WhoAmIAsync();
        _tokenPolicy.MinSerial = _tokenSerial + 1; // 吊销既有令牌

        var result = await client.WhoAmIAsync();

        Assert.NotNull(result);
        Assert.Equal(2, _tokenRequests);
    }

    [Fact]
    public async Task 无用户上下文_以服务自身身份调用()
    {
        await using var caller = CreateCaller(currentUser: null);
        var client = caller.GetRequiredService<IDemoClient>();

        var result = await client.WhoAmIAsync();

        Assert.NotNull(result);
        Assert.Null(result.UserId);
        Assert.Equal(ClientId, result.ClientId);
    }

    [Fact]
    public async Task 远端业务错误_还原为RemoteServiceException含远端traceId()
    {
        await using var caller = CreateCaller(new FakeCurrentUser(id: Guid.NewGuid()));
        var client = caller.GetRequiredService<IDemoClient>();

        var exception = await Assert.ThrowsAsync<RemoteServiceException>(client.FailAsync);

        Assert.Equal(404, exception.RemoteStatusCode);
        Assert.Equal("Order:NotFound", exception.ErrorCode);
        Assert.Equal("remote-trace-42", exception.RemoteTraceId);
        Assert.Contains("订单不存在", exception.Message);
    }

    [Fact]
    public async Task 匿名请求伪造用户头_被调方剥离后不污染ICurrentUser()
    {
        using var rawClient = _resourceHost.GetTestServer().CreateClient();
        rawClient.DefaultRequestHeaders.Add(ServiceClientHeaders.UserId, Guid.NewGuid().ToString());

        var response = await rawClient.GetAsync("/api/echo-user");
        var result = await response.ReadResultAsync<WhoAmI>();

        Assert.NotNull(result);
        Assert.Null(result.UserId);
    }
}
