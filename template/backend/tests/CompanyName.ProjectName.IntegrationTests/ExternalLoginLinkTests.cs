#if (ExternalLogin)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.UnitOfWork;
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
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("ExternalAuth:AlreadyLinked", await ErrorCodeAsync(taken));
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
            Assert.Equal("ExternalAuth:LastSignInMethod", await ErrorCodeAsync(rejected));
        }

        // 设有密码的用户随时可以解绑
        provider.User = External("gh-with-password");
        var username = await CreateUserAsync(host, "link_unlink");
        using var withPassword = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        using (var link = await LinkAsync(host, withPassword))
        {
            Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        }

        var stampBefore = await ReadSecurityStampAsync(host, username);
        using var unlink = await withPassword.Client.DeleteAsync($"/api/v1/external-auth/links/{LinkIdOf(await ReadLinksAsync(withPassword.Client))}");
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        Assert.Equal(JsonValueKind.Undefined, GithubLink(await ReadLinksAsync(withPassword.Client)).ValueKind);
        // 解绑是凭据变化：安全版本随之轮换并落库，凭这个外部账号完成第一步、尚未完成的登录挑战随之作废
        Assert.NotEqual(stampBefore, await ReadSecurityStampAsync(host, username));
    }

    /// <summary>
    /// 外部登录按邮箱关联已有账号，要求提供商确认邮箱已验证、且本地账号邮箱也已确认；
    /// 任一边没验证而邮箱已被占用时拒绝，不建新号、不留绑定，提示先登录再绑定。
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task External_sign_in_links_by_email_only_when_both_sides_are_verified(bool providerVerified, bool localConfirmed)
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        var username = await CreateUserAsync(host, "link_email");
        if (localConfirmed)
        {
            using var scope = host.Services.CreateScope();
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
            var user = await users.GetOneAsync(u => u.Username == username);
            user!.ConfirmEmail();
            await users.UpdateAsync(user);
            await unitOfWork.CompleteAsync();
        }

        provider.User = External("gh-by-email") with
        {
            Email = $"{username}@example.test",
            EmailVerified = providerVerified
        };
        using var external = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(external, "/api/v1/external-auth/github/login-url");
        external.DefaultRequestHeaders.Add("Cookie", cookie);
        using var callback = await external.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });

        using var session = await ProjectWebApplicationFactory.LoginAsync(host, username, Password);
        var link = GithubLink(await ReadLinksAsync(session.Client));
        if (providerVerified && localConfirmed)
        {
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            Assert.Equal("gh-by-email", link.GetProperty("providerUsername").GetString());
            return;
        }

        Assert.Equal(HttpStatusCode.Conflict, callback.StatusCode);
        Assert.Equal("ExternalAuth:AccountExistsSignInToLink", await ErrorCodeAsync(callback));
        Assert.Equal(JsonValueKind.Undefined, link.ValueKind);
    }

    /// <summary>
    /// 未验证的外部邮箱不写进新账号：否则就占用了别人的地址，本人随后注册会被挡、找回时接手的是对方建的号。
    /// </summary>
    [Fact]
    public async Task Unverified_external_email_is_not_taken_by_a_new_account()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        const string victimEmail = "victim@example.test";

        provider.User = External("gh-squatter") with { Email = victimEmail, EmailVerified = false };
        using var callback = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using var signedIn = ProjectWebApplicationFactory.CreateProjectClient(host);
        signedIn.DefaultRequestHeaders.Add("Cookie", CookieOf(callback));
        var me = await signedIn.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.NotEqual(victimEmail, me.GetProperty("email").GetString());

        // 地址的主人仍能用它建号
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = "victim_owner",
            Email = victimEmail,
            Password,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
    }

    /// <summary>
    /// 以已验证邮箱新建的账号记为已确认：本人再用另一个外部账号（同一已验证邮箱）登录，关联回同一个用户，而不是被拒。
    /// </summary>
    [Fact]
    public async Task Account_created_from_a_verified_email_links_the_next_verified_sign_in()
    {
        using var factory = new ProjectWebApplicationFactory();
        var provider = new SwitchableOAuthProvider();
        using var host = CreateHost(factory, provider);
        const string email = "owner@example.test";

        provider.User = External("gh-first") with { Email = email, EmailVerified = true };
        using var first = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        firstClient.DefaultRequestHeaders.Add("Cookie", CookieOf(first));
        var firstMe = await firstClient.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(email, firstMe.GetProperty("email").GetString());

        provider.User = External("gh-second") with { Email = email, EmailVerified = true };
        using var second = await SignInExternallyAsync(host);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        secondClient.DefaultRequestHeaders.Add("Cookie", CookieOf(second));
        var secondMe = await secondClient.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(firstMe.GetProperty("id").GetGuid(), secondMe.GetProperty("id").GetGuid());
    }

    private static async Task<HttpResponseMessage> SignInExternallyAsync(WebApplicationFactory<Program> host)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var (state, cookie) = await StartAsync(client, "/api/v1/external-auth/github/login-url");
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return await client.PostAsJsonAsync("/api/v1/external-auth/github/callback", new { Code = "code", State = state });
    }

    private static async Task<string> ReadSecurityStampAsync(WebApplicationFactory<Program> host, string username)
    {
        using var scope = host.Services.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var user = await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>().GetOneAsync(u => u.Username == username);
        return user!.SecurityStamp;
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
