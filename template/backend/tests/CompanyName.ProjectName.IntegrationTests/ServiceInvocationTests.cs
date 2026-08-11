#if (IncludeOpenIddict)
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 服务间调用闭环：client_credentials 换取令牌 → 携带受信 X-User-Id 调用 →
/// 被调方恢复用户上下文（ICurrentUser + ICurrentClient 双通道）；伪造头被阻断。
/// </summary>
public sealed class ServiceInvocationTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    private const string CallerClientId = "svc-caller";
    private const string CallerClientSecret = "SvcCaller@123456";

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

        // 1. client_credentials 换取访问令牌（与真实调用方 SDK 相同的标准形态）
        var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = CallerClientId,
                ["client_secret"] = CallerClientSecret,
            }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenDto>();
        Assert.False(string.IsNullOrEmpty(token!.access_token));

        // 2. 携带 Bearer + 受信用户头调用：恢复的用户与调用方 client 同时可见
        var adminId = await GetAdminIdAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/service-info/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.access_token);
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

        var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = CallerClientId,
                ["client_secret"] = CallerClientSecret,
            }));
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenDto>();

        // 默认策略要求可用的自然人用户：纯工作负载身份（无用户上下文）不满足
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/service-info/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.access_token);

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
