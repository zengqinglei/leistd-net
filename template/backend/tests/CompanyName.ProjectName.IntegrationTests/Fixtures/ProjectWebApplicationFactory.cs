using System.Net;
using System.Net.Http.Json;
#if (RemoteTokenAuth)
using CompanyName.ProjectName.Api.HealthChecks;
using CompanyName.ProjectName.Api.HostedServices.Initializer;
#endif
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#if (!LocalIdentity)
using System.Security.Claims;
using System.Text.Encodings.Web;
using CompanyName.ProjectName.Api.Auth;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
#endif
using CompanyName.ProjectName.Domain.Users.Policies;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 集成测试宿主：单一内存库。
/// </summary>
/// <remarks>
/// <para>本夹具覆盖不分库形态下的业务逻辑。<b>它不覆盖租户专属数据库的路由</b>——
/// <c>Infrastructure/DependencyInjection.cs</c> 按"有没有连接串"二选一：有就 <c>UseNpgsql</c>，
/// 没有就内存库。因此在这里给租户登记连接会走到 <c>UseNpgsql</c> 去连一个不存在的库，
/// 不要那样写用例。</para>
/// <para>租户分库的路由与隔离要用真实 PostgreSQL 的集成测试覆盖：
/// 断言共享租户的数据落在默认库、专属租户的数据落在自己的库，且两边互不泄漏。</para>
/// </remarks>
public sealed class ProjectWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// 测试宿主的超级管理员密码。唯一定义处
    /// </summary>
    /// <remarks>
    /// 以字面量散在各调用点时，改动测试凭据要逐处追平；集中之后是改一行。
    /// 取值要满足密码策略（见 <c>PasswordPolicy</c>），否则测试宿主自己就起不来。
    /// </remarks>
    public const string TestAdminPassword = "IntegrationTests!Adm1n";

    private readonly string databaseName = $"ProjectTests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // 注册阶段就被读取的键（选数据库分支、选锁实现）必须经 UseSetting 注入：
        // 下面 ConfigureAppConfiguration 加的配置在 Program 注册服务时还不可见，
        // 放在那里会被静默忽略——测试宿主会走真实库分支、按开发机的环境变量去连 Redis，
        // 而不是走生产里同一套内存库注册路径。
        builder.UseSetting("ConnectionStrings:Default", "");
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Database:InMemoryName", databaseName);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SpaProxy:Enabled"] = "false",
                ["OAuth:DisableHttpsRequirement"] = "true",
                ["DefaultAdmin:Username"] = "admin",
                ["DefaultAdmin:Password"] = TestAdminPassword,
#if (LocalIdentity)
                // 固定值即可：测试要的是确定性，不是保密性
                ["VerificationCodes:Key"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
#endif
                ["UserRegistration:EnableEmailVerification"] = "false",
#if (RemoteTokenAuth)
                // 启动期校验要求它存在（它决定改路由前的排空等待），测试宿主给个确定值
                ["TenantRouting:CacheLifetime"] = "00:10:00",
#endif
            });
        });

        // 测试宿主关闭确定化：强制托管服务顺序启停（关闭并发路径），避免 WebApplicationFactory 释放时
        // Host.ForeachService 与已释放的 CancellationTokenSource 竞争，导致类清理期偶发 ObjectDisposedException。
        // 作用于工厂 → 覆盖所有测试类（Health / Localization / Notifications 等），而非逐类修补。
        builder.ConfigureServices(services =>
        {
#if (RemoteTokenAuth)
            // 测试宿主里没有真实 Identity，启动探针永远探不通。这里直接把门禁置为已开：
            // 其它用例要测的是业务端点，不是"等 Identity 就绪"这件事。
            // 门禁本身的语义（未确认前拒绝流量、确认后锁存）由 ResourceReadinessGateTests 单独钉住
            // 只摘这一个托管服务：RemoveAll<IHostedService>() 会把 ApplicationInitializer
            // 一起摘掉，那是其它用例赖以初始化的东西
            var probe = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(RemoteIdentityReadinessInitializer));
            if (probe is not null)
            {
                services.Remove(probe);
            }

            var openedGate = new RemoteIdentityReadinessHealthCheck();
            openedGate.MarkReady();
            services.AddSingleton(openedGate);
#endif
#if (!LocalIdentity)
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
                var testPolicy = new AuthorizationPolicyBuilder(ResourceTestAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build();
                options.DefaultPolicy = testPolicy;
                // 组件端点按名字要这条策略（不套默认策略），替身方案必须把它一起换掉，
                // 否则通知中心、读设置这些自用端点仍然要求生产 Bearer，测试里一律 401
                options.AddPolicy(ApiPolicies.CurrentUser, testPolicy);
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

#if (LocalIdentity)
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

#if (!LocalIdentity)
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

#if (!LocalIdentity)
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
