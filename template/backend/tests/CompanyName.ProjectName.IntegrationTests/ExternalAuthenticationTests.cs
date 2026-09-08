#if (ExternalLogin)
using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.Auth;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class ExternalAuthenticationTests
{
    private const string StateCookieName = "__Host-CompanyName.ProjectName.ExternalAuth.State";

    [Fact]
    public async Task Login_url_issues_a_protected_state_cookie_and_mismatched_state_is_rejected()
    {
        using var factory = new ProjectWebApplicationFactory();
        using var host = CreateExternalAuthHost(factory, new ExternalUserInfo
        {
            ProviderId = "state-user",
            Username = "state-user",
            Email = "state-user@example.com"
        });
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);

        var challenge = await StartExternalLoginAsync(client);

        Assert.Contains("HttpOnly", challenge.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", challenge.SetCookie, StringComparison.OrdinalIgnoreCase);

        // 断言"与会话 Cookie 同策略"，不是断言某个字面值。
        // 生产用 SameSite=None（模板支持前后端分离部署），状态 Cookie 若固定为 Lax，
        // 那种部署下 login-url 是跨站 XHR，浏览器根本不会保存它，回调必然失败。
        // 测试宿主跑在 Development（Lax），写死字面值既测不到生产形态，
        // 又会把两者分叉这件事钉成契约
        var sessionSameSite = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationSchemeNames.SessionCookie).Cookie.SameSite;
        Assert.Contains(
            $"samesite={sessionSameSite}",
            challenge.SetCookie,
            StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Add("Cookie", challenge.Cookie);
        var callback = await client.PostAsJsonAsync(
            "/api/v1/external-auth/github/callback",
            new { Code = "valid-code", State = "mismatched-state" });

        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
    }

    [Fact]
    public async Task Successful_callback_consumes_state_and_replay_is_rejected()
    {
        using var factory = new ProjectWebApplicationFactory();
        using var host = CreateExternalAuthHost(factory, new ExternalUserInfo
        {
            ProviderId = "replay-user",
            Username = "replay-user",
            Email = "replay-user@example.com"
        });
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);

        var challenge = await StartExternalLoginAsync(client);
        client.DefaultRequestHeaders.Add("Cookie", challenge.Cookie);

        var callback = await client.PostAsJsonAsync(
            "/api/v1/external-auth/github/callback",
            new { Code = "valid-code", challenge.State });

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Contains(
            callback.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith($"{StateCookieName}=", StringComparison.Ordinal) &&
                     value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        using var replayClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        replayClient.DefaultRequestHeaders.Add("Cookie", challenge.Cookie);
        var replay = await replayClient.PostAsJsonAsync(
            "/api/v1/external-auth/github/callback",
            new { Code = "valid-code", challenge.State });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Tenant_external_login_cookie_preserves_the_tenant_without_a_request_header()
    {
        const string tenantName = "external-tenant";
        const string tenantEmail = "admin@external-tenant.example.com";
        using var factory = new ProjectWebApplicationFactory();
        using var host = CreateExternalAuthHost(factory, new ExternalUserInfo
        {
            ProviderId = "tenant-admin",
            Username = "tenant-admin",
            Email = tenantEmail
        });
        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(
            host,
            "admin",
            ProjectWebApplicationFactory.TestAdminPassword);

        var createTenant = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = tenantName,
            DisplayName = "External Tenant",
            AdminEmail = tenantEmail,
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.OK, createTenant.StatusCode);
        var tenant = await createTenant.Content.ReadFromJsonAsync<TenantIdResponse>();
        Assert.NotNull(tenant);

        using var externalClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        externalClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.Id.ToString());
        var challenge = await StartExternalLoginAsync(externalClient);
        externalClient.DefaultRequestHeaders.Add("Cookie", challenge.Cookie);
        var callback = await externalClient.PostAsJsonAsync(
            "/api/v1/external-auth/github/callback",
            new { Code = "valid-code", challenge.State });
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

        var authCookie = callback.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("CompanyName.ProjectName.Auth=", StringComparison.Ordinal))
            .Split(';', 2)[0];
        using var sessionClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        sessionClient.DefaultRequestHeaders.Add("Cookie", authCookie);

        var users = await sessionClient.GetFromJsonAsync<UserPageResponse>("/api/v1/users?offset=0&limit=10");
        Assert.NotNull(users);
        Assert.Single(users.Items);
        Assert.Equal(tenantEmail, users.Items[0].Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_locked_accounts_cannot_sign_in_through_an_external_provider(bool locked)
    {
        const string adminEmail = "admin@companyname-projectname.com";
        using var factory = new ProjectWebApplicationFactory();
        using var host = CreateExternalAuthHost(factory, new ExternalUserInfo
        {
            ProviderId = locked ? "locked-admin" : "disabled-admin",
            Username = "admin",
            Email = adminEmail
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var admin = await dbContext.Set<User>().SingleAsync(user => user.Email == adminEmail);
            if (locked)
            {
                admin.Lock();
            }
            else
            {
                admin.Disable();
            }

            await dbContext.SaveChangesAsync();
        }

        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var challenge = await StartExternalLoginAsync(client);
        client.DefaultRequestHeaders.Add("Cookie", challenge.Cookie);
        var callback = await client.PostAsJsonAsync(
            "/api/v1/external-auth/github/callback",
            new { Code = "valid-code", challenge.State });

        Assert.Equal(HttpStatusCode.Unauthorized, callback.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateExternalAuthHost(
        ProjectWebApplicationFactory factory,
        ExternalUserInfo externalUser)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalAuth:Github:ClientId"] = "integration-test-client",
                    ["ExternalAuth:Github:ClientSecret"] = "integration-test-secret",
                    ["ExternalAuth:Github:RedirectUri"] = "https://client.example.test/auth/callback/github"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOAuthProvider>();
                services.AddSingleton<IOAuthProvider>(new StubOAuthProvider(externalUser));
            });
        });
    }

    private static async Task<ExternalLoginChallenge> StartExternalLoginAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/external-auth/github/login-url");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var output = await response.Content.ReadFromJsonAsync<ExternalLoginUrlResponse>();
        Assert.NotNull(output);

        // state 不在响应体里：它由提供商回显到回调地址，客户端从 loginUrl 之外拿不到，
        // 也不需要拿到——绑定靠 HttpOnly Cookie
        var state = QueryHelpers.ParseQuery(new Uri(output.LoginUrl).Query)["state"].ToString();
        Assert.False(string.IsNullOrEmpty(state));

        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith($"{StateCookieName}=", StringComparison.Ordinal));
        return new ExternalLoginChallenge(state, setCookie.Split(';', 2)[0], setCookie);
    }

    private sealed class StubOAuthProvider(ExternalUserInfo externalUser) : IOAuthProvider
    {
        public string Name => "github";

        public string GetAuthorizationUrl(string redirectUri, string state) =>
            $"https://provider.example.test/authorize?state={Uri.EscapeDataString(state)}";

        public Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
            string code,
            string redirectUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OAuthTokenInfo { AccessToken = "integration-test-token" });

        public Task<ExternalUserInfo> GetUserInfoAsync(
            string accessToken,
            CancellationToken cancellationToken = default) => Task.FromResult(externalUser);
    }

    private sealed record ExternalLoginChallenge(string State, string Cookie, string SetCookie);
    private sealed record ExternalLoginUrlResponse(string LoginUrl);
    private sealed record TenantIdResponse(Guid Id);
    private sealed record UserPageResponse(IReadOnlyList<UserResponse> Items);
    private sealed record UserResponse(string Email);
}
#endif
