#if (IncludeOpenIddict)
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using CompanyName.ProjectName.Client;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Security.Claims;
using Leistd.ServiceClient.Exceptions;
using Leistd.Security.Users;
using Leistd.Tracing.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 服务间调用闭环。分两组：
/// <list type="bullet">
/// <item>被调方安全：受信恢复用户上下文、纯工作负载不满足默认策略、伪造头被剥离；</item>
/// <item>调用方消费路径：另起一个调用方宿主，经真实 Client 包（Add{ProjectName}Client →
/// 全局 ServiceAuth 绑定 → Refit → 标准管道 → DTO 契约）完成调用。</item>
/// </list>
/// </summary>
public sealed class ServiceInvocationTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    private const string CallerClientId = "svc-caller";
    private const string CallerClientSecret = "SvcCaller@123456";

    // ---------- 被调方安全 ----------

    [Fact]
    public async Task Service_info_should_be_anonymous()
    {
        using var client = factory.CreateProjectClient();

        var response = await client.GetAsync("/api/v1/service-info");

        response.EnsureSuccessStatusCode();
        var info = await response.Content.ReadFromJsonAsync<ServiceInfoDto>();
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.Service));
    }

    [Fact]
    public async Task Service_call_with_client_credentials_should_restore_user_context()
    {
        await EnsureCallerRegisteredAsync();
        using var client = CreateHttpsClient();
        var token = await GetMachineTokenAsync(client);

        var adminId = await GetAdminIdAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/service-info/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-User-Id", adminId.ToString());

        var whoAmIResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, whoAmIResponse.StatusCode);
        var whoAmI = await whoAmIResponse.Content.ReadFromJsonAsync<WhoAmIDto>();
        Assert.Equal(adminId, whoAmI!.UserId);
        Assert.Equal(CallerClientId, whoAmI.ClientId);
    }

    [Fact]
    public async Task Service_call_without_user_header_should_not_satisfy_default_policy()
    {
        await EnsureCallerRegisteredAsync();
        using var client = CreateHttpsClient();
        var token = await GetMachineTokenAsync(client);

        // 默认策略要求可用的自然人用户：纯工作负载身份（无用户上下文）不满足
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/service-info/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Forged_user_header_without_service_token_should_be_rejected()
    {
        using var client = factory.CreateProjectClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/service-info/whoami");
        request.Headers.Add("X-User-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // 匿名请求的伪造头被剥离，不产生任何身份
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Machine_token_subject_should_not_collide_with_user_ids()
    {
        await EnsureCallerRegisteredAsync();
        using var client = CreateHttpsClient();
        var token = await GetMachineTokenAsync(client);

        // 机器主体的 sub 走 ClientSubject 契约（client:<client_id>），与用户 GUID 命名空间不相交，
        // 因此 client_id 取任何值都无法被解析成某个自然人。
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/service-info/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-User-Id", (await GetAdminIdAsync()).ToString());
        var whoAmI = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<WhoAmIDto>();

        Assert.Equal(CallerClientId, whoAmI!.ClientId);
        Assert.False(Guid.TryParse(ClientSubject.Format(CallerClientId), out _));
    }

    // ---------- 调用方消费路径（经真实 Client 包） ----------

    [Fact]
    public async Task Client_package_should_call_anonymous_endpoint()
    {
        await using var caller = CreateCallerHost();

        var info = await caller.GetRequiredService<IMyProjectClient>().GetServiceInfoAsync();

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.Service));
    }

    [Fact]
    public async Task Client_package_should_forward_user_context_end_to_end()
    {
        await EnsureCallerRegisteredAsync();
        var adminId = await GetAdminIdAsync();
        await using var caller = CreateCallerHost();

        // 调用方以某个用户的身份发起：SDK 自动注入 X-User-Id，被调方受信恢复
        var accessor = caller.GetRequiredService<ICurrentPrincipalAccessor>();
        using (accessor.Change(CreateUserPrincipal(adminId)))
        {
            var whoAmI = await caller.GetRequiredService<IMyProjectClient>().WhoAmIAsync();

            Assert.Equal(adminId, whoAmI!.UserId);
            Assert.Equal(CallerClientId, whoAmI.ClientId);
        }
    }

    [Fact]
    public async Task Client_package_should_surface_remote_errors_as_remote_service_exception()
    {
        await EnsureCallerRegisteredAsync();
        await using var caller = CreateCallerHost();

        // 无用户上下文 → 被调方默认策略拒绝；Refit 的错误经统一 ExceptionFactory 还原
        var exception = await Assert.ThrowsAsync<RemoteServiceException>(
            () => caller.GetRequiredService<IMyProjectClient>().WhoAmIAsync());

        Assert.Equal((int)HttpStatusCode.Forbidden, exception.StatusCode);
    }

    /// <summary>
    /// 调用方宿主：只注册 Client 包所需的服务，HTTP 走被调方 TestServer 的处理器。
    /// 这是「另一个业务服务引用本服务 Client 包」的最小等价形态。
    /// </summary>
    private ServiceProvider CreateCallerHost()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // 本服务（调用方）的调用身份，全局一次
            ["Leistd:ServiceAuth:TokenEndpoint"] = "https://localhost/connect/token",
            ["Leistd:ServiceAuth:ClientId"] = CallerClientId,
            ["Leistd:ServiceAuth:ClientSecret"] = CallerClientSecret,
            // 目标服务：地址（+ 可选 scope）
            ["Leistd:ServiceClients:MyProject:BaseAddress"] = "https://localhost",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCorrelationIdCore(_ => { });
        services.AddSingleton<ICurrentPrincipalAccessor, TestCurrentPrincipalAccessor>();
        services.AddTransient<ICurrentUser, CurrentUser>();

        services.AddMyProjectClient(configuration)
            .ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
        services.AddHttpClient(Leistd.ServiceClient.OAuth.Services.ClientCredentialsTokenProvider.TokenHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUserPrincipal(Guid userId) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestCaller"));

    /// <summary>调用方宿主里的主体访问器：无 HTTP 上下文，只认 <c>Change</c> 显式设定的主体。</summary>
    private sealed class TestCurrentPrincipalAccessor : CurrentPrincipalAccessor
    {
        protected override ClaimsPrincipal? GetClaimsPrincipal() => null;
    }

    // ---------- 辅助 ----------

    /// <summary>
    /// OpenIddict 的令牌端点只收 HTTPS。TestServer 不做真实 TLS，改基地址即可让
    /// <c>Request.IsHttps</c> 成立，不必为测试在服务端放宽这条要求。
    /// </summary>
    private HttpClient CreateHttpsClient()
    {
        var client = factory.CreateProjectClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private static async Task<string> GetMachineTokenAsync(HttpClient client)
    {
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = CallerClientId,
                ["client_secret"] = CallerClientSecret,
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.False(string.IsNullOrEmpty(token!.access_token));
        return token.access_token;
    }

    private async Task EnsureCallerRegisteredAsync()
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync(CallerClientId) is null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = CallerClientId,
                ClientSecret = CallerClientSecret,
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                },
            });
        }
    }

    private async Task<Guid> GetAdminIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Users.Where(u => u.Username == "admin").Select(u => u.Id).SingleAsync();
    }

    private sealed record ServiceInfoDto(string Service, string Version, DateTimeOffset ServerTime);

    private sealed record WhoAmIDto(Guid? UserId, string? UserName, string? ClientId);

    private sealed record TokenDto(string access_token);
}
#endif
