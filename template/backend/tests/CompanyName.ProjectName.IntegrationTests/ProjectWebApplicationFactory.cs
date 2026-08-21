using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
#if (ResourceService)
using System.Security.Claims;
using System.Text.Encodings.Web;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
#endif
#if (MultiTenancy)
using Leistd.MultiTenancy;
#endif

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class ProjectWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"ProjectTests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "",
                ["ConnectionStrings:Redis"] = "",
                ["Database:InMemoryName"] = databaseName,
                ["SpaProxy:Enabled"] = "false",
                ["OAuth:DisableHttpsRequirement"] = "true",
                ["DefaultAdmin:Username"] = "admin",
                ["DefaultAdmin:Password"] = "Admin@123456",
                ["UserRegistration:EnableEmailVerification"] = "false"
            });
        });

        // 测试宿主关闭确定化：强制托管服务顺序启停（关闭并发路径），避免 WebApplicationFactory 释放时
        // Host.ForeachService 与已释放的 CancellationTokenSource 竞争，导致类清理期偶发 ObjectDisposedException。
        // 作用于工厂 → 覆盖所有测试类（Health / Localization / Notifications 等），而非逐类修补。
        builder.ConfigureServices(services =>
        {
#if (MultiTenancy)
            // 集成测试显式使用 EF InMemory，不经生产的 Identity/Secret 路由链。
            services.RemoveAll<ITenantConnectionStringResolver>();
#endif
#if (ResourceService)
            // Resource 模板不托管登录端点。集成测试以专用方案注入已验证主体，
            // 不伪造生产 Bearer 验签，也不让 Resource 回退为本地 Cookie 登录。
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = ResourceTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = ResourceTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, ResourceTestAuthenticationHandler>(
                    ResourceTestAuthenticationHandler.SchemeName,
                    _ => { });
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(ResourceTestAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build();
            });
#endif
            services.Configure<HostOptions>(options =>
            {
                options.ServicesStartConcurrently = false;
                options.ServicesStopConcurrently = false;
            });
        });
    }

    public HttpClient CreateProjectClient() => CreateProjectClient(this);

    /// <summary>
    /// 供 <see cref="WebApplicationFactory{T}.WithWebHostBuilder"/> 派生出的宿主复用。
    /// 那个方法返回的是基类类型，拿不到本类的实例成员。
    /// </summary>
    public static HttpClient CreateProjectClient(WebApplicationFactory<Program> host)
    {
        return host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

#if (IdentityService)
    public Task<AuthenticatedSession> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
        => LoginAsync(this, username, password, cancellationToken);

    public static async Task<AuthenticatedSession> LoginAsync(
        WebApplicationFactory<Program> host,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var client = CreateProjectClient(host);
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = username, Password = password },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0]));
        Assert.False(string.IsNullOrWhiteSpace(cookie));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new AuthenticatedSession(client, cookie);
    }
#endif

#if (ResourceService)
    public AuthenticatedSession CreateResourceSession(Guid subjectId, Guid tenantId) =>
        CreateResourceSession(this, subjectId, tenantId);

    public static AuthenticatedSession CreateResourceSession(
        WebApplicationFactory<Program> host,
        Guid subjectId,
        Guid tenantId)
    {
        var headers = new Dictionary<string, string>
        {
            [ResourceTestAuthenticationHandler.SubjectHeader] = subjectId.ToString(),
            [ResourceTestAuthenticationHandler.TenantHeader] = tenantId.ToString()
        };
        var client = CreateProjectClient(host);
        foreach (var (name, value) in headers)
            client.DefaultRequestHeaders.Add(name, value);

        return new AuthenticatedSession(client, string.Empty, headers);
    }
#endif
}

public sealed class AuthenticatedSession(
    HttpClient client,
    string cookie,
    IReadOnlyDictionary<string, string>? authenticationHeaders = null) : IDisposable
{
    public HttpClient Client { get; } = client;
    public string Cookie { get; } = cookie;
    public IReadOnlyDictionary<string, string> AuthenticationHeaders { get; } =
        authenticationHeaders ?? new Dictionary<string, string>();

    public void Dispose() => Client.Dispose();
}

#if (ResourceService)
internal sealed class ResourceTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ProjectTests";
    public const string SubjectHeader = "X-Project-Test-Subject";
    public const string TenantHeader = "X-Project-Test-Tenant";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers[SubjectHeader].SingleOrDefault();
        var tenant = Request.Headers[TenantHeader].SingleOrDefault();
        if (!Guid.TryParse(subject, out var subjectId) || !Guid.TryParse(tenant, out var tenantId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            [
                new Claim("sub", subjectId.ToString()),
                new Claim(CustomClaimTypes.TenantId, tenantId.ToString())
            ],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
#endif
