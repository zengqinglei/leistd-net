#if (OpenIddictServer)
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 租户用户走完整个 OIDC 流程：授权、授权码换令牌、userinfo、刷新令牌。
/// </summary>
/// <remarks>
/// 令牌端点与 userinfo 的请求不带用户会话，请求本身只能解析出宿主；用户与租户必须取自令牌主体，
/// 在其租户内加载用户。取错上下文时租户用户在宿主分区里查不到，只在租户用户身上失败——宿主用户的用例照样绿。
/// </remarks>
public sealed class TenantOidcFlowTests(ProjectWebApplicationFactory factory)
    : AuthorizationTestBase(factory), IClassFixture<ProjectWebApplicationFactory>
{
    private const string TenantAdminPassword = "Tenant@123456";

    [Fact]
    public Task A_tenant_user_can_exchange_a_code_read_userinfo_and_refresh() => RunTenantFlowAsync(Factory);

    /// <summary>
    /// 宿主把主体标识换成别的 claim 名：签发、换码、userinfo、资源服务判定自然人与机器主体都按它走。
    /// </summary>
    /// <remarks>令牌只签 sub 的话，换码按配置读不到用户、资源服务判不出自然人，链路在签发方自己这里就断了。</remarks>
    [Fact]
    public async Task A_configured_user_id_claim_carries_through_the_whole_flow()
    {
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<ClaimTypeOptions>(options => options.UserIds = ["user_id"])));

        await RunTenantFlowAsync(host);

        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var machine = await CreateMachineClientAsync(host, hostAdmin.Client);
        var databases = await machine.GetAsync("/api/v1/tenant-connections/databases?name=Default");
        Assert.NotEqual(HttpStatusCode.Unauthorized, databases.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, databases.StatusCode);
    }

    private async Task RunTenantFlowAsync(WebApplicationFactory<Program> host)
    {
        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var tenantId = await CreateTenantAsync(hostAdmin.Client, $"oidc{Guid.CreateVersion7():N}"[..16]);
        var (clientId, clientSecret) = await CreateClientAsync(hostAdmin.Client);
        using var tenantSession = await LoginTenantAdminAsync(host, tenantId);

        var tokens = await AuthorizeAndExchangeAsync(host, tenantSession, clientId, clientSecret);

        using var api = CreateHttpsClient(host);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var userInfo = await api.GetAsync("/connect/userinfo");
        Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
        using (var body = JsonDocument.Parse(await userInfo.Content.ReadAsStringAsync()))
        {
            Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("sub").GetString()));
        }

        // 资源服务按默认策略判定自然人
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/auth/me")).StatusCode);

        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));
        using var refresh = CreateHttpsClient(host);
        var refreshed = await refresh.PostAsync("/connect/token", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", tokens.RefreshToken!),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret)
        ]));
        Assert.True(refreshed.IsSuccessStatusCode, await refreshed.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateTenantAsync(HttpClient hostAdmin, string name)
    {
        var response = await hostAdmin.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = name,
            DisplayName = $"{name} Inc.",
            AdminEmail = $"admin@{name}.example.com",
            AdminPassword = TenantAdminPassword
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<AuthenticatedSession> LoginTenantAdminAsync(WebApplicationFactory<Program> host, Guid tenantId)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.DefaultRequestHeaders.Add(MultiTenancyOptions.DefaultHeaderName, tenantId.ToString());
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = "admin", Password = TenantAdminPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0]));
        return new AuthenticatedSession(client, cookie);
    }

    private static async Task<(string ClientId, string ClientSecret)> CreateClientAsync(HttpClient hostAdmin)
    {
        var clientId = $"client-{Guid.CreateVersion7():N}";
        var created = await hostAdmin.PostAsJsonAsync("/api/v1/open-applications", new
        {
            clientId,
            displayName = "Tenant OIDC probe",
            applicationType = "web",
            clientType = "confidential",
            consentType = "explicit",
            permissions = new[]
            {
                "ept:authorization", "ept:token", "gt:authorization_code", "gt:refresh_token", "rst:code",
                "scp:openid", "scp:offline_access"
            },
            requirements = Array.Empty<string>(),
            redirectUris = new[] { RedirectUri },
            postLogoutRedirectUris = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var application = await created.Content.ReadFromJsonAsync<OpenApplicationOutputDto>();
        Assert.NotNull(application);

        var reset = await hostAdmin.PostAsync($"/api/v1/open-applications/{application.Id}/reset-secret", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var secret = await reset.Content.ReadFromJsonAsync<ResetOpenApplicationSecretOutputDto>();
        Assert.NotNull(secret);
        return (clientId, secret.ClientSecret);
    }

    // 授权端点凭会话 Cookie 直接签发授权码（无同意页）；PKCE 必须是真的 S256
    private static async Task<TokenResponse> AuthorizeAndExchangeAsync(
        WebApplicationFactory<Program> host, AuthenticatedSession session, string clientId, string clientSecret)
    {
        var verifier = Guid.CreateVersion7().ToString("N") + Guid.CreateVersion7().ToString("N");
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        using var browser = CreateHttpsClient(host);
        browser.DefaultRequestHeaders.Add("Cookie", session.Cookie);
        var authorize = await browser.GetAsync(
            "/connect/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=code&scope=openid%20offline_access" +
            $"&code_challenge={challenge}&code_challenge_method=S256");
        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);

        var code = authorize.Headers.Location!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "code")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .Single();

        using var exchange = CreateHttpsClient(host);
        var token = await exchange.PostAsync("/connect/token", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("code_verifier", verifier)
        ]));
        Assert.True(token.IsSuccessStatusCode, await token.Content.ReadAsStringAsync());

        var payload = await token.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(payload);
        return payload;
    }

    /// <summary>注册一个带 tenant-routing.read 的 client_credentials 应用，返回已带 Bearer 的客户端。</summary>
    private static async Task<HttpClient> CreateMachineClientAsync(WebApplicationFactory<Program> host, HttpClient hostAdmin)
    {
        var clientId = $"client-{Guid.CreateVersion7():N}";
        var created = await hostAdmin.PostAsJsonAsync("/api/v1/open-applications", new
        {
            clientId,
            displayName = "Workload",
            applicationType = "service",
            clientType = "confidential",
            consentType = "explicit",
            permissions = new[] { "ept:token", "gt:client_credentials", "scp:tenant-routing.read" },
            requirements = Array.Empty<string>(),
            redirectUris = Array.Empty<string>(),
            postLogoutRedirectUris = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var application = await created.Content.ReadFromJsonAsync<OpenApplicationOutputDto>();
        Assert.NotNull(application);
        var reset = await hostAdmin.PostAsync($"/api/v1/open-applications/{application.Id}/reset-secret", null);
        var secret = await reset.Content.ReadFromJsonAsync<ResetOpenApplicationSecretOutputDto>();
        Assert.NotNull(secret);

        var machine = CreateHttpsClient(host);
        var token = await machine.PostAsync("/connect/token", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", secret.ClientSecret),
            new KeyValuePair<string, string>("scope", "tenant-routing.read")
        ]));
        Assert.True(token.IsSuccessStatusCode, await token.Content.ReadAsStringAsync());
        var payload = await token.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(payload);
        machine.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        return machine;
    }

    // OpenIddict 的授权与令牌端点只收 HTTPS；改基地址即可让 Request.IsHttps 成立
    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> host)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);
}
#endif
