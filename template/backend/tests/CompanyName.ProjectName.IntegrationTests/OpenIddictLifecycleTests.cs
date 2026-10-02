#if (OpenIddictServer)
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Json;
using CompanyName.ProjectName.Application.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.BackgroundJobs.Recurring;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class OpenIddictLifecycleTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Callback = "https://localhost/oidc-test";

    private async Task<string> CreatePublicClientAsync(HttpClient admin)
    {
        var id = $"prompt-{Guid.NewGuid():N}";
        var response = await admin.PostAsJsonAsync("/api/v1/open-applications", new
        {
            clientId = id, applicationType = "web", clientType = "public", redirectUris = new[] { Callback },
            permissions = new[] { "ept:authorization", "ept:token", "gt:authorization_code", "rst:code", "scp:openid", $"scp:{new OAuthOptions().Resource}" },
            requirements = new[] { "ft:pkce" }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var output = await response.Content.ReadFromJsonAsync<OpenApplicationOutputDto>();
        Assert.NotNull(output);
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await manager.FindByClientIdAsync(id);
        Assert.Equal(ConsentTypes.Implicit, await manager.GetConsentTypeAsync(app!));
        return id;
    }

    private HttpClient Browser(string? cookie = null)
    {
        var client = factory.CreateProjectClient();
        client.BaseAddress = new Uri("https://localhost");
        if (cookie is not null) client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static string AuthorizationUrl(string client, string parameters) =>
        $"/connect/authorize?client_id={client}&redirect_uri={Uri.EscapeDataString(Callback)}&response_type=code" +
        $"&scope=openid%20{new OAuthOptions().Resource}&code_challenge={Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(new string('x', 64))))}&code_challenge_method=S256{parameters}";

    [Fact]
    public async Task Prompt_none_without_a_session_returns_login_required_to_the_protocol_callback()
    {
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var id = await CreatePublicClientAsync(admin.Client);
        using var anonymous = Browser();
        var response = await anonymous.GetAsync(AuthorizationUrl(id, "&prompt=none&state=probe"));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(Callback, response.Headers.Location!.AbsoluteUri);
        Assert.Contains("error=login_required", response.Headers.Location.Query);
        Assert.Contains("state=probe", response.Headers.Location.Query);
        Assert.DoesNotContain("/auth/login", response.Headers.Location.AbsoluteUri);
    }

    [Theory]
    [InlineData("&prompt=login")]
    [InlineData("&max_age=0")]
    public async Task Reauthentication_preserves_the_session_until_credentials_succeed_and_does_not_loop(string parameter)
    {
        using var session = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var id = await CreatePublicClientAsync(session.Client);
        using var browser = Browser(session.Cookie);
        var response = await browser.GetAsync(AuthorizationUrl(id, parameter));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/auth/login?returnUrl=", location);
        Assert.EndsWith("&reauthenticate=true", location);
        var encodedReturn = location[(location.IndexOf("returnUrl=", StringComparison.Ordinal) + 10)..].Split('&')[0];
        var returnUrl = Uri.UnescapeDataString(encodedReturn);
        Assert.DoesNotContain("prompt=login", returnUrl);
        Assert.DoesNotContain("max_age=0", returnUrl);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        // 人为提供旧 auth_time，避免依赖秒级等待；续期不会改变真实认证时间。
        var oldTicket = ReadCookie(session.Cookie);
        var oldIdentity = (ClaimsIdentity)oldTicket.Principal.Identity!;
        foreach (var claim in oldIdentity.FindAll(Claims.AuthenticationTime).ToArray()) oldIdentity.RemoveClaim(claim);
        var oldTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        oldIdentity.AddClaim(new Claim(Claims.AuthenticationTime, oldTime.ToString(), ClaimValueTypes.Integer64));
        using var relogin = Browser(CookieOf(oldTicket));
        using var failed = await relogin.PostAsJsonAsync("/api/v1/auth/session-login", new { usernameOrEmail = "admin", password = "incorrect" });
        Assert.False(failed.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        using var success = await relogin.PostAsJsonAsync("/api/v1/auth/session-login", new
        { usernameOrEmail = "admin", password = ProjectWebApplicationFactory.TestAdminPassword });
        Assert.True(success.IsSuccessStatusCode, await success.Content.ReadAsStringAsync());
        var cookie = success.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(session.Cookie.Split('=')[0] + "=", StringComparison.Ordinal));
        var renewed = ReadCookie(cookie);
        Assert.True(long.Parse(renewed.Principal.GetClaim(Claims.AuthenticationTime)!) > oldTime);
        using var authenticated = Browser(cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await authenticated.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    private AuthenticationTicket ReadCookie(string cookie)
    {
        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationSchemeNames.SessionCookie);
        var value = cookie.Split(';')[0].Split('=', 2)[1];
        var reference = cookieOptions.TicketDataFormat.Unprotect(value)!;
        return cookieOptions.SessionStore!.RetrieveAsync(reference.Principal.FindFirst("Microsoft.AspNetCore.Authentication.Cookies-SessionId")!.Value).GetAwaiter().GetResult()!;
    }

    private string CookieOf(AuthenticationTicket ticket)
    {
        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationSchemeNames.SessionCookie);
        var key = ticket.Properties.Items["ticket.key"]!;
        cookieOptions.SessionStore!.RenewAsync(key, ticket).GetAwaiter().GetResult();
        var reference = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("Microsoft.AspNetCore.Authentication.Cookies-SessionId", key)], AuthenticationSchemeNames.SessionCookie)),
            ticket.Properties, AuthenticationSchemeNames.SessionCookie);
        return cookieOptions.Cookie.Name + "=" + cookieOptions.TicketDataFormat.Protect(reference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Max_age_uses_original_authentication_time_even_after_cookie_renewal(bool missingTime)
    {
        using var session = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var id = await CreatePublicClientAsync(session.Client);
        var ticket = ReadCookie(session.Cookie);
        var identity = (ClaimsIdentity)ticket.Principal.Identity!;
        foreach (var claim in identity.FindAll(Claims.AuthenticationTime).ToArray()) identity.RemoveClaim(claim);
        if (!missingTime)
            identity.AddClaim(new Claim(Claims.AuthenticationTime, DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow;
        using var browser = Browser(CookieOf(ticket));
        var response = await browser.GetAsync(AuthorizationUrl(id, "&prompt=none&max_age=30"));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("error=login_required", response.Headers.Location!.Query);
        Assert.StartsWith(Callback, response.Headers.Location.AbsoluteUri);
    }

    [Fact]
    public async Task Identity_token_auth_time_comes_from_cookie_and_is_a_numeric_date()
    {
        using var session = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var id = await CreatePublicClientAsync(session.Client);
        var ticket = ReadCookie(session.Cookie);
        var original = long.Parse(ticket.Principal.GetClaim(Claims.AuthenticationTime)!);
        ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow.AddSeconds(10);
        using var browser = Browser(CookieOf(ticket));
        var response = await browser.GetAsync(AuthorizationUrl(id, "&max_age=3600"));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var code = response.Headers.Location!.Query.TrimStart('?').Split('&')
            .Single(part => part.StartsWith("code=", StringComparison.Ordinal))[5..];
        using var exchange = Browser();
        var token = await exchange.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = id,
            ["code"] = Uri.UnescapeDataString(code), ["redirect_uri"] = Callback, ["code_verifier"] = new string('x', 64)
        }));
        Assert.True(token.IsSuccessStatusCode, await token.Content.ReadAsStringAsync());
        using var responseJson = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
        var jwt = responseJson.RootElement.GetProperty("id_token").GetString()!.Split('.')[1];
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt));
        Assert.Equal(original, claims.RootElement.GetProperty("auth_time").GetInt64());
    }

    [Fact]
    public async Task Pruning_uses_registered_cluster_job_and_keeps_young_or_valid_records()
    {
        // 走官方的批量清理（ExecuteDelete），与生产一致。批量删除不经过变更跟踪器，
        // 同一作用域里仍能读到已删的实体，因此清理后换一个作用域读库里的真实状态
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<OpenIddictCoreOptions>(options => options.DisableEntityCaching = true)));
        using var scope = host.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var now = DateTimeOffset.UtcNow;
        async Task<object> Token(DateTimeOffset created, DateTimeOffset expiry) => await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = Guid.NewGuid().ToString(), Type = TokenTypeHints.AccessToken, Status = Statuses.Valid,
            CreationDate = created, ExpirationDate = expiry
        });
        var oldExpired = await Token(now.AddDays(-15), now.AddDays(-1));
        var youngExpired = await Token(now.AddDays(-13), now.AddDays(-1));
        var oldValid = await Token(now.AddDays(-15), now.AddDays(1));
        async Task<object> Authorization(DateTimeOffset created, string status, string type) => await authorizations.CreateAsync(new OpenIddictAuthorizationDescriptor
        {
            Subject = Guid.NewGuid().ToString(), Type = type, Status = status, CreationDate = created
        });
        var oldRevoked = await Authorization(now.AddDays(-15), Statuses.Revoked, AuthorizationTypes.Permanent);
        var youngRevoked = await Authorization(now.AddDays(-13), Statuses.Revoked, AuthorizationTypes.Permanent);
        var oldPermanent = await Authorization(now.AddDays(-15), Statuses.Valid, AuthorizationTypes.Permanent);
        var oldAdHoc = await Authorization(now.AddDays(-15), Statuses.Valid, AuthorizationTypes.AdHoc);
        var definition = scope.ServiceProvider.GetServices<RecurringJobDefinition>().Single(job => job.Name == "auth.openiddict.prune");
        Assert.Equal(RecurringJobScope.Cluster, definition.Scope);
        var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(definition.JobType);
        var oldExpiredId = (await tokens.GetIdAsync(oldExpired))!;
        var youngExpiredId = (await tokens.GetIdAsync(youngExpired))!;
        var oldValidId = (await tokens.GetIdAsync(oldValid))!;
        var oldRevokedId = (await authorizations.GetIdAsync(oldRevoked))!;
        var youngRevokedId = (await authorizations.GetIdAsync(youngRevoked))!;
        var oldPermanentId = (await authorizations.GetIdAsync(oldPermanent))!;
        var oldAdHocId = (await authorizations.GetIdAsync(oldAdHoc))!;
        await job.ExecuteAsync(new RecurringJobContext(definition.Name, now), default);

        using var verify = host.Services.CreateScope();
        var storedTokens = verify.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var storedAuthorizations = verify.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        Assert.Null(await storedTokens.FindByIdAsync(oldExpiredId));
        Assert.Null(await storedAuthorizations.FindByIdAsync(oldRevokedId));
        Assert.Null(await storedAuthorizations.FindByIdAsync(oldAdHocId));
        Assert.NotNull(await storedTokens.FindByIdAsync(youngExpiredId));
        Assert.NotNull(await storedTokens.FindByIdAsync(oldValidId));
        Assert.NotNull(await storedAuthorizations.FindByIdAsync(youngRevokedId));
        Assert.NotNull(await storedAuthorizations.FindByIdAsync(oldPermanentId));
    }
}
#endif
