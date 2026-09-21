#if (ExternalLogin)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 外部账号绑定：已登录用户绑定 / 解绑外部账号，绑定与登录的授权凭据互不通用。
/// </summary>
public sealed class ExternalLoginLinkTests
{
    private const string StateCookieName = "__Host-CompanyName.ProjectName.ExternalAuth.State";
    private const string Password = "LinkTests!Passw0rd";

    [Fact]
    public async Task Linked_external_account_signs_in_to_the_same_user()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        var username = await CreateUserAsync(host, "link_ok");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

        provider.User = External("gh-link-ok");
        using (var link = await LinkAsync(host, session))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        var links = await ReadLinksAsync(session.Client);
        var github = links.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("provider").GetString() == "github");
        Assert.Equal("gh-link-ok", github.GetProperty("link").GetProperty("providerUsername").GetString());

        // 用这个外部账号登录，进来的是绑定它的那个用户，而不是新建一个
        using var external = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(external, "/api/v1/external-auth/github/login-url");
        external.DefaultRequestHeaders.Add("Cookie", cookie);
        using var callback = await external.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using var signedIn = ProjectWebApplicationFactory.CreateProjectClient(host);
        signedIn.DefaultRequestHeaders.Add("Cookie", CookieOf(callback));
        var me = await signedIn.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(username, me.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Sign_in_and_link_authorizations_are_not_interchangeable()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider { User = External("gh-cross") };
        using var host = CreateHost(factory, provider);
        var username = await CreateUserAsync(host, "link_cross");
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);

        // 登录 state → 绑定端点
        var (loginState, loginCookie) = await StartAsync(session.Client, "/api/v1/external-auth/github/login-url");
        using (var misuse = await PostWithCookiesAsync(host, $"{session.Cookie}; {loginCookie}",
                   "/api/v1/external-auth/github/link", new { Code = "code", State = loginState }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, misuse.StatusCode);
        }

        // 绑定 state → 登录回调
        var (linkState, linkCookie) = await StartAsync(session.Client, "/api/v1/external-auth/github/link-url");
        using (var misuse = await PostWithCookiesAsync(host, linkCookie,
                   "/api/v1/external-auth/github/callback", new { Code = "code", State = linkState }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, misuse.StatusCode);
        }
    }

    [Fact]
    public async Task Rejects_an_external_account_linked_to_another_user()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider { User = External("gh-taken") };
        using var host = CreateHost(factory, provider);

        using var first = await ProjectWebApplicationFactory.LoginAsync(host, await CreateUserAsync(host, "link_first"), Password);
        using (var link = await LinkAsync(host, first))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        using var second = await ProjectWebApplicationFactory.LoginAsync(host, await CreateUserAsync(host, "link_second"), Password);
        using var taken = await LinkAsync(host, second);
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal(ExpectedErrorCode.Of("ExternalAuth:AlreadyLinked", "Error:BadRequest"), await ErrorCodeAsync(taken));
    }

    [Fact]
    public async Task Last_external_login_can_be_unlinked_only_with_a_password()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider { User = External("gh-only") };
        using var host = CreateHost(factory, provider);

        // 经外部登录建出来的账号没有密码
        using var external = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(external, "/api/v1/external-auth/github/login-url");
        external.DefaultRequestHeaders.Add("Cookie", cookie);
        using var callback = await external.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });
        using var externalOnly = ProjectWebApplicationFactory.CreateProjectClient(host);
        externalOnly.DefaultRequestHeaders.Add("Cookie", CookieOf(callback));

        var links = await ReadLinksAsync(externalOnly);
        Assert.False(links.GetProperty("hasPassword").GetBoolean());
        var linkId = LinkIdOf(links);
        using (var rejected = await externalOnly.DeleteAsync($"/api/v1/external-auth/links/{linkId}"))
        {
            Assert.Equal(ExpectedErrorCode.Of("ExternalAuth:LastSignInMethod", "Error:BadRequest"), await ErrorCodeAsync(rejected));
        }

        // 设有密码的用户随时可以解绑
        provider.User = External("gh-with-password");
        using var withPassword = await ProjectWebApplicationFactory.LoginAsync(host, await CreateUserAsync(host, "link_unlink"), Password);
        using (var link = await LinkAsync(host, withPassword))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        using var unlink = await withPassword.Client.DeleteAsync($"/api/v1/external-auth/links/{LinkIdOf(await ReadLinksAsync(withPassword.Client))}");
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        Assert.Equal(JsonValueKind.Undefined, GithubLink(await ReadLinksAsync(withPassword.Client)).ValueKind);
    }

    private static WebApplicationFactory<Program> CreateHost(ProjectWebApplicationFactory factory, SwitchableOAuthProvider provider) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalAuth:Github:ClientId"] = "integration-test-client",
                    ["ExternalAuth:Github:ClientSecret"] = "integration-test-secret",
                    ["ExternalAuth:Github:RedirectUri"] = "https://client.example.test/auth/external-callback"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOAuthProvider>();
                services.AddSingleton<IOAuthProvider>(provider);
            });
        });

    private static async Task<HttpResponseMessage> LinkAsync(WebApplicationFactory<Program> host, AuthenticatedSession session)
    {
        var (state, stateCookie) = await StartAsync(session.Client, "/api/v1/external-auth/github/link-url");
        return await PostWithCookiesAsync(host, $"{session.Cookie}; {stateCookie}",
            "/api/v1/external-auth/github/link", new { Code = "code", State = state });
    }

    private static async Task<(string State, string Cookie)> StartAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var state = QueryHelpers.ParseQuery(new Uri(body.RootElement.GetProperty("loginUrl").GetString()!).Query)["state"].ToString();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith($"{StateCookieName}=", StringComparison.Ordinal))
            .Split(';', 2)[0];
        return (state, cookie);
    }

    private static async Task<HttpResponseMessage> PostWithCookiesAsync(
        WebApplicationFactory<Program> host, string cookies, string url, object body)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Cookie", cookies);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadLinksAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/external-auth/links"));
        return body.RootElement.Clone();
    }

    private static JsonElement GithubLink(JsonElement links) =>
        links.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == "github")
            .TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.Object ? link : default;

    private static Guid LinkIdOf(JsonElement links) => GithubLink(links).GetProperty("id").GetGuid();

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string CookieOf(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Where(value => !value.StartsWith($"{StateCookieName}=", StringComparison.Ordinal))
            .Select(value => value.Split(';', 2)[0]));

    private static async Task<string> CreateUserAsync(WebApplicationFactory<Program> host, string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}"[..30];
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = username,
            Email = $"{username}@example.test",
            Password,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        return username;
    }

    private static ExternalUserInfo External(string id) => new()
    {
        ProviderId = id,
        Username = id,
        Email = $"{id}@external.example.test"
    };

    /// <summary>测试替身：每次授权"回来"的外部身份由用例指定。</summary>
    private sealed class SwitchableOAuthProvider : IOAuthProvider
    {
        public ExternalUserInfo User { get; set; } = new() { ProviderId = "unset", Username = "unset" };

        public string Name => "github";

        public bool IsAvailable => true;

        public string GetAuthorizationUrl(string state) =>
            $"https://provider.example.test/authorize?state={Uri.EscapeDataString(state)}";

        public Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
            string code,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OAuthTokenInfo { AccessToken = "integration-test-token" });

        public Task<ExternalUserInfo> GetUserInfoAsync(
            string accessToken,
            CancellationToken cancellationToken = default) => Task.FromResult(User);
    }
}
#endif
