using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Tenants;
using CompanyName.ProjectName.Application.Tenants.AppServices;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Authorization;
using Leistd.Authorization.EntityFrameworkCore;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Authorization.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.EntityFrameworkCore.Managers;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.Abstractions;
using Leistd.Timing;
using Leistd.UnitOfWork;
#if (ExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Entities;
#endif

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 多租户端到端闭环：建租户即种子、租户登录、数据硬隔离、宿主侧权限边界、停用即拒。
/// </summary>
public sealed class TenancyTests : IClassFixture<ProjectWebApplicationFactory>, IDisposable
{
    private readonly ProjectWebApplicationFactory _factory;
    private readonly List<IDisposable> _disposables = [];

    public TenancyTests(ProjectWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private async Task<AuthenticatedSession> LoginHostAdminAsync()
    {
        var session = await _factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        _disposables.Add(session);
        return session;
    }

    /// <summary>宿主管理员建租户，返回租户 Id。</summary>
    private async Task<Guid> CreateTenantAsync(AuthenticatedSession hostAdmin, string name)
    {
        var response = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = name,
            DisplayName = $"{name} Inc.",
            AdminEmail = $"admin@{name.ToLowerInvariant()}.example.com",
            AdminPassword = "Tenant@123456"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>租户管理员登录：登录请求与后续请求都携带 X-Tenant-Id。</summary>
    private Task<HttpClient> LoginTenantAdminAsync(Guid tenantId, string password = "Tenant@123456")
        => LoginTenantAdminAsync(_factory, tenantId, password);

    /// <summary>在指定宿主上做租户管理员登录（<c>WithWebHostBuilder</c> 派生的宿主用这个重载）。</summary>
    private async Task<HttpClient> LoginTenantAdminAsync(
        WebApplicationFactory<Program> host, Guid tenantId, string password = "Tenant@123456")
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        _disposables.Add(client);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = "admin", Password = password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0]));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    /// <summary>
    /// 以匿名身份带租户头调用注册端点，返回状态码。
    /// </summary>
    /// <remarks>
    /// 注册是匿名端点，正是"半成品租户"最现实的入侵面：租户一旦启用，
    /// 任何人带上它的 X-Tenant-Id 就能在还没有管理员的租户里注册出第一个用户。
    /// </remarks>
    private static async Task<HttpStatusCode> ProbeAnonymousRegistrationAsync(
        WebApplicationFactory<Program> host, Guid tenantId)
    {
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = "intruder",
            Email = "intruder@example.com",
            Password = "Intruder@123456"
        });

        return response.StatusCode;
    }

    private static async Task<List<string>> GetUsernamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/users?offset=0&limit=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("username").GetString()!)
            .ToList();
    }

    /// <summary>
    /// 租户管理员是<b>普通用户 + Admin 角色</b>，不是超管
    /// </summary>
    /// <remarks>
    /// <para><c>IsSuperAdmin</c> 是宿主的防锁死逃生舱：旁路功能权限、资源实例授权与数据范围过滤，
    /// 且不可停用、不可删除、没有撤销入口。给租户用户打上它会带来三个真实后果——</para>
    /// <para>1. 任何直接读 <c>is_super_admin</c> claim、不经权限检查器的判定点
    /// （自定义授权策略就是这种）会失去侧别边界，成为跨租户越权；</para>
    /// <para>2. 该用户在自己租户内无法被停用或删除；</para>
    /// <para>3. 数据范围与资源实例授权在租户内被整体旁路。</para>
    /// <para>租户不需要逃生舱：Admin 角色已带全部 Tenant 侧权限（含权限管理本身），
    /// 升级后新增的权限由租户管理员自己授给自己的角色。</para>
    /// </remarks>
    [Fact]
    public async Task 租户管理员不是超管_权限由Admin角色承载()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "roleonly");

        using var scope = _factory.Services.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(tenantId))
        {
            var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
            var tenantAdmin = await users.GetFirstAsync(u => u.Username == "admin");

            Assert.NotNull(tenantAdmin);
            Assert.False(tenantAdmin.IsSuperAdmin);

            Assert.True(tenantAdmin.CanBeDisabled());
            Assert.True(tenantAdmin.CanBeDeleted());

            var userRoles = scope.ServiceProvider.GetRequiredService<IRepository<UserRole, Guid>>();
            Assert.True(await userRoles.AnyAsync(ur => ur.UserId == tenantAdmin.Id));
        }
    }

    /// <summary>租户管理员能行使 Tenant 侧权限，但取不到任何宿主侧权限</summary>
    [Fact]
    public async Task 租户管理员有本租户权限_但无宿主侧权限()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "hostsideguard");
        var tenantClient = await LoginTenantAdminAsync(tenantId);

        var roles = await tenantClient.GetAsync("/api/v1/roles?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);

        var tenants = await tenantClient.GetAsync("/api/v1/tenants?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.Forbidden, tenants.StatusCode);
    }

    /// <summary>领域服务拒绝在租户上下文里造超管</summary>
    /// <remarks>
    /// 这一条只覆盖领域服务这道关。数据库那道兜底（检查约束 <c>CK_User_SuperAdminIsHostOnly</c>，
    /// 挡的是数据修复脚本、批量导入与直接 SQL）用的是真实 PostgreSQL 才生效的 DDL，
    /// 本套用例跑在内存提供程序上，验证它的地方是 <c>test-template-postgresql-e2e.ps1</c>。
    /// </remarks>
    [Fact]
    public async Task 领域服务拒绝在租户上下文创建超管()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "noescape");

        using var scope = _factory.Services.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var users = scope.ServiceProvider
            .GetRequiredService<CompanyName.ProjectName.Domain.Users.DomainServices.UserDomainService>();

        using (currentTenant.Change(tenantId))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                users.CreateSuperAdminAsync(
                    "escape-hatch",
                    "escape@example.com",
                    "Tenant@123456",
                    displayName: null,
                    passwordSubject: "test"));

            Assert.Contains(tenantId.ToString(), error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task 建租户即种子_租户管理员可登录并只见本租户用户()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "acme");

        using var anonymous = _factory.CreateProjectClient();
        var lookup = await anonymous.GetAsync("/api/v1/tenants/by-name/ACME");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);

        var tenantClient = await LoginTenantAdminAsync(tenantId);
        var usernames = await GetUsernamesAsync(tenantClient);

        // 只见本租户种子的 admin，见不到宿主 admin（同名但分属不同分区）
        Assert.Contains("admin", usernames);
        Assert.Single(usernames);
    }

    [Fact]
    public async Task 宿主视角只见宿主用户_伪造租户头无法改写已登录会话()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "isolation-a");

        var hostUsernames = await GetUsernamesAsync(hostAdmin.Client);
        Assert.Contains("admin", hostUsernames);
        Assert.DoesNotContain(hostUsernames, name => name.StartsWith("isolation"));

        // 已认证会话携带伪造租户头：claim 定案为宿主，头无法把会话挪进租户
        using var forged = _factory.CreateProjectClient();
        forged.DefaultRequestHeaders.Add("Cookie", hostAdmin.Cookie);
        forged.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        var forgedUsernames = await GetUsernamesAsync(forged);
        Assert.Equal(hostUsernames.Order(), forgedUsernames.Order());
    }

    [Fact]
    public async Task 跨租户按Id取数不可见_表现为404()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantAId = await CreateTenantAsync(hostAdmin, "cross-a");
        var tenantBId = await CreateTenantAsync(hostAdmin, "cross-b");

        var tenantAClient = await LoginTenantAdminAsync(tenantAId);
        var tenantBClient = await LoginTenantAdminAsync(tenantBId);

        var bUsers = await tenantBClient.GetAsync("/api/v1/users?offset=0&limit=10");
        using var bBody = JsonDocument.Parse(await bUsers.Content.ReadAsStringAsync());
        var bAdminId = bBody.RootElement.GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        // 租户 A 管理员按 Id 访问租户 B 的用户：过滤器使其不可见，统一表现为 404
        var crossDetail = await tenantAClient.GetAsync($"/api/v1/users/{bAdminId}");
        Assert.Equal(HttpStatusCode.NotFound, crossDetail.StatusCode);
    }

    [Fact]
    public async Task 租户管理员的current权限不含宿主侧权限_菜单据此裁剪()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "sidecheck");
        var tenantClient = await LoginTenantAdminAsync(tenantId);

        // 租户管理员的 current 权限集与检查器同口径：宿主侧权限不下发，
        // 前端菜单/路由据此裁剪，不会出现点进去必然 403 的宿主功能
        var current = await tenantClient.GetAsync("/api/v1/permissions/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var body = await current.Content.ReadAsStringAsync();
        Assert.DoesNotContain("App.Tenants", body);
        Assert.Contains("App.Users", body);

        var hostCurrent = await hostAdmin.Client.GetAsync("/api/v1/permissions/current");
        Assert.Contains("App.Tenants", await hostCurrent.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 租户管理员不能访问租户管理API_宿主侧权限对租户拒绝()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "boundary");

        var tenantClient = await LoginTenantAdminAsync(tenantId);

        var list = await tenantClient.GetAsync("/api/v1/tenants?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var create = await tenantClient.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "evil",
            AdminEmail = "evil@example.com",
            AdminPassword = "TenancyTests!Evil"
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task 停用租户后_在途会话与再登录均被拒()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "frozen");

        var tenantClient = await LoginTenantAdminAsync(tenantId);
        Assert.Single(await GetUsernamesAsync(tenantClient));

        // 停用在提交那一刻生效（存储直接读库），在途会话的下一个请求就被拒
        var deactivate = await hostAdmin.Client.PutAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/activation", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // XHR/API 请求：会话自恢复中间件注销 Cookie 并回 401（前端按会话失效清理本地状态）
        var afterDeactivation = await tenantClient.GetAsync("/api/v1/users?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDeactivation.StatusCode);

        // 带租户失效标记：前端据此把它与"普通会话过期"区分开，只在这种 401 上清掉已选租户。
        // 不带的话前端只能二选一——要么每次超时都强迫重选租户，要么跳回登录页仍带着已死的租户
        Assert.True(afterDeactivation.Headers.Contains("X-Tenant-Invalid"));

        // 反面：普通未认证 401 不带这个头，否则判据失效、前端又会在每次会话过期时清掉租户
        using var anonymous = _factory.CreateProjectClient();
        var ordinary = await anonymous.GetAsync("/api/v1/users?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.Unauthorized, ordinary.StatusCode);
        Assert.False(ordinary.Headers.Contains("X-Tenant-Invalid"));

        // HTML 导航：注销后重定向回原地址，下一次请求已匿名、SPA 可正常加载——用户不会死锁在错误页
        using var navRequest = new HttpRequestMessage(HttpMethod.Get, "/platform/users");
        navRequest.Headers.Accept.ParseAdd("text/html");
        foreach (var header in tenantClient.DefaultRequestHeaders)
        {
            navRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var navClient = _factory.CreateProjectClient();
        var navResponse = await navClient.SendAsync(navRequest);
        Assert.Equal(HttpStatusCode.Redirect, navResponse.StatusCode);

        using var relogin = _factory.CreateProjectClient();
        relogin.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        var loginResponse = await relogin.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = "admin", Password = "Tenant@123456" });
        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
    }

    /// <summary>
    /// 启用一个不存在的租户回 404，不能被"租户内没有用户"的守卫抢先讲成 400。
    /// </summary>
    [Fact]
    public async Task 启用不存在的租户返回404()
    {
        var hostAdmin = await LoginHostAdminAsync();

        var response = await hostAdmin.Client.PutAsJsonAsync(
            $"/api/v1/tenants/{Guid.NewGuid()}/activation", new { IsActive = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// 跨域响应必须把 <c>X-Tenant-Invalid</c> 列入 Access-Control-Expose-Headers。
    /// </summary>
    /// <remarks>
    /// CORS 分离部署（前端 dev server 直连后端）是模板明确支持的模式之一。
    /// 自定义响应头不在 CORS 安全清单里，不显式暴露则浏览器不交给 JS——
    /// 后端照常发了头、前端 <c>error.headers.get()</c> 恒为 null，租户失效恢复静默失效。
    /// 这条断言是必要的：后端集成测试直读 TestServer 响应头、前端单测手工构造 HttpHeaders，
    /// 仅断言原始响应头存在会漏掉它；本用例带真实 Origin 并断言 Access-Control-Expose-Headers，
    /// 因此协议层就能发现。浏览器负责的是另一半：前端读到该头、清租户状态、完成跳转的闭环。
    /// </remarks>
    [Fact]
    public async Task 跨域响应暴露租户失效标记头()
    {
        // 复刻"模式二：CORS 分离访问"的配置：默认 AllowAnyLocalhost=false 时不放行任何来源，
        // CORS 中间件不会写任何响应头，这条断言也就无从谈起
        using var corsHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowAnyLocalhost"] = "true"
                })));

        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(corsHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var tenantId = await CreateTenantAsync(hostAdmin, "cors-check");
        using var tenantClient = await LoginTenantAdminAsync(corsHost, tenantId);

        var deactivate = await hostAdmin.Client.PutAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/activation", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users?offset=0&limit=10");
        request.Headers.Add("Origin", "http://localhost:4200");
        foreach (var header in tenantClient.DefaultRequestHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var browserLike = ProjectWebApplicationFactory.CreateProjectClient(corsHost);
        var response = await browserLike.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Tenant-Invalid"));

        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("X-Tenant-Invalid", exposed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 子域名部署下，来自不受信任地址的 X-Forwarded-Host 不能改写租户。
    /// </summary>
    /// <remarks>
    /// <para><c>UseForwardedHeaders</c> 会用 <c>X-Forwarded-Host</c> 覆盖 <c>Request.Host</c>，
    /// 而 Host 正是子域名解析的权威来源，且转发头中间件排在多租户中间件之前。
    /// 把 <c>KnownProxies</c>/<c>KnownIPNetworks</c> 清空（=接受任何客户端的转发头）时，
    /// "子域名是匿名请求权威来源"这条边界就形同虚设。</para>
    /// <para>判据用停用租户：把 b 停掉，转发头若被采信就会解析到 b 并 403；
    /// 没被采信则留在 a（或宿主）而 200。用回显不了租户的端点做断言等于什么都没测。</para>
    /// <para>这条必须在模板层：它验证的是宿主管道的组合（转发头信任 × 解析链），
    /// 框架侧那条"请求头改不动子域名"的用例测不到宿主的代理信任配置。</para>
    /// </remarks>
    [Fact]
    public async Task 不受信任来源的转发头不能改写子域名解析出的租户()
    {
        var hostAdmin = await LoginHostAdminAsync();
        await CreateTenantAsync(hostAdmin, "subdomain-a");
        var blockedId = await CreateTenantAsync(hostAdmin, "subdomain-b");

        var deactivate = await hostAdmin.Client.PutAsJsonAsync(
            $"/api/v1/tenants/{blockedId}/activation", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using var domainHost = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Leistd:MultiTenancy:DomainFormat"] = "{0}.example.com"
                }));

            // TestServer 的客户端地址是环回，而环回在框架默认信任集里——不改的话
            // 转发头会被正常采信，这条用例观测不到目标属性。换成公网测试地址，
            // 模拟"直连的不受信任客户端"
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new UntrustedRemoteAddressFilter()));
        });

        using var client = ProjectWebApplicationFactory.CreateProjectClient(domainHost);

        using var forged = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/security-config");
        forged.Headers.Host = "subdomain-a.example.com";
        forged.Headers.TryAddWithoutValidation("X-Forwarded-Host", "subdomain-b.example.com");

        var response = await client.SendAsync(forged);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// 正向：配置为可信代理的来源，其转发头应当被采信。
    /// </summary>
    /// <remarks>
    /// 只有"不可信来源被拒"这一条时，把信任逻辑改成永不生效也能让它保持绿——
    /// 那样网关后的 X-Forwarded-* 全部失效却无人察觉。两个方向都要钉住。
    /// </remarks>
    [Fact]
    public async Task 可信代理的转发头会被采信()
    {
        var hostAdmin = await LoginHostAdminAsync();
        await CreateTenantAsync(hostAdmin, "trusted-a");
        var blockedId = await CreateTenantAsync(hostAdmin, "trusted-b");

        var deactivate = await hostAdmin.Client.PutAsJsonAsync(
            $"/api/v1/tenants/{blockedId}/activation", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using var domainHost = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Leistd:MultiTenancy:DomainFormat"] = "{0}.example.com",
                    ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.10"
                }));

            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new UntrustedRemoteAddressFilter()));
        });

        using var client = ProjectWebApplicationFactory.CreateProjectClient(domainHost);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/security-config");
        request.Headers.Host = "trusted-a.example.com";
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "trusted-b.example.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 未知租户404_名称大小写不敏感()
    {
        using var anonymous = _factory.CreateProjectClient();

        var unknownLookup = await anonymous.GetAsync($"/api/v1/tenants/by-name/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, unknownLookup.StatusCode);

        anonymous.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        var unknownTenantRequest = await anonymous.GetAsync("/api/v1/auth/security-config");
        Assert.Equal(HttpStatusCode.NotFound, unknownTenantRequest.StatusCode);
    }

    /// <summary>
    /// 种子进行中的租户必须还没启用：否则它已经能被中间件接受，而此刻还没有管理员和权限授予。
    /// </summary>
    [Fact]
    public async Task 种子进行中租户尚未启用_匿名注册进不去半成品租户()
    {
        using var probingHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddTransient<TenantSeeder>();
                services.AddTransient<ITenantSeeder, ProbingTenantSeeder>();
            }));

        ProbingTenantSeeder.Reset();
        ProbingTenantSeeder.Probe = tenantId => ProbeAnonymousRegistrationAsync(probingHost, tenantId);

        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(probingHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var tenantId = await CreateTenantAsync(hostAdmin, "provisioning");

        // 探测发生在种子中途（角色、管理员都还没写完）：中间件必须拒绝这个尚未启用的租户
        Assert.Equal(HttpStatusCode.Forbidden, ProbingTenantSeeder.ProbedStatus);

        // 种子成功之后才激活：此刻租户正常可用，管理员能登录且只见自己的种子用户
        using var tenantClient = await LoginTenantAdminAsync(probingHost, tenantId);
        Assert.Equal(["admin"], await GetUsernamesAsync(tenantClient));
    }

    [Fact]
    public async Task 外层工作单元存在时_租户创建的三个阶段仍使用独立边界()
    {
        using var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TenantCreationUnitOfWorkProbe>();
                services.AddTransient<EfCoreTenantManager<IdentityControlDbContext>>();
                services.AddTransient<ITenantManager, RecordingTenantManager>();
                services.AddTransient<TenantSeeder>();
                services.AddTransient<ITenantSeeder, RecordingTenantSeeder>();
            }));

        using var scope = host.Services.CreateScope();
        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var outerUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);

        var name = $"outer-{Guid.NewGuid():N}";
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantAppService>().CreateAsync(
            new CreateTenantInputDto
            {
                Name = name,
                DisplayName = $"{name} Inc.",
                AdminEmail = $"admin@{name}.example.com",
                AdminPassword = "Tenant@123456"
            });

        Assert.True(tenant.IsActive);
        Assert.Same(outerUnitOfWork, unitOfWorkManager.Current);

        var observations = scope.ServiceProvider
            .GetRequiredService<TenantCreationUnitOfWorkProbe>()
            .Observations;
        Assert.Equal(["control", "business", "activation"], observations.Select(item => item.Phase));
        Assert.All(observations, item => Assert.NotEqual(outerUnitOfWork.Id, item.UnitOfWorkId));
        Assert.Equal(3, observations.Select(item => item.UnitOfWorkId).Distinct().Count());
    }

    /// <summary>
    /// 播种成功但激活失败：整个创建回滚，不留下"数据完整却永远停用"的租户。
    /// </summary>
    /// <remarks>
    /// 激活失败**必须**走补偿，不能因为"种子数据已完整"就放过：激活失败的现实成因之一
    /// 是另一个宿主管理员在播种期间删掉了这个租户，那时数据不是完整的而是孤儿的
    /// （落在一个软删租户 Id 下、永远不可达）。激活只有"租户不存在"和"数据库故障"两种失败，
    /// 两种情形下租户都不可用，因此统一补偿——比留一个需要人工判断的中间态干净。
    /// </remarks>
    [Fact]
    public async Task 激活失败时整个创建回滚_不留下停用的孤儿租户()
    {
        using var brokenActivationHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddTransient<EfCoreTenantManager<IdentityControlDbContext>>();
                services.AddTransient<ITenantManager, FailActivationTenantManager>();
            }));

        FailActivationTenantManager.Reset();

        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(
            brokenActivationHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var failed = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "halfway",
            AdminEmail = "admin@halfway.example.com",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        var tenantId = FailActivationTenantManager.LastCreatedId;
        Assert.NotNull(tenantId);

        // 补偿覆盖了一次完整成功的播种：角色、授予、管理员全部清掉
        await AssertNoVisibleTenantDataAsync(brokenActivationHost, tenantId.Value);

        using var anonymous = ProjectWebApplicationFactory.CreateProjectClient(brokenActivationHost);
        var lookup = await anonymous.GetAsync("/api/v1/tenants/by-name/halfway");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    /// <summary>
    /// 清种子失败不能拖累删注册表：两步各用一个作用域。
    /// </summary>
    /// <remarks>
    /// 删注册表是"租户从此不可达"的最后一道保障。共用作用域时，清种子那一步的
    /// SaveChanges 失败会在跟踪器里留下实体，删注册表的 SaveChanges 会把它们一起写出去，
    /// 甚至因它们再次失败——于是租户既没清干净、又留在注册表里对外可用。
    /// </remarks>
    [Fact]
    public async Task 清种子失败时注册表仍被删除_补偿两步互不牵连()
    {
        using var brokenPurgeHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddTransient<ITenantSeeder, FailingPurgeTenantSeeder>()));

        FailingPurgeTenantSeeder.Reset();

        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(
            brokenPurgeHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var failed = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "purge-broken",
            AdminEmail = "admin@purge-broken.example.com",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(FailingPurgeTenantSeeder.PurgeAttempted, "补偿没有尝试清种子，本用例没覆盖目标路径");

        // 清种子抛错且留下脏跟踪器，注册表删除仍然成功：租户不可达
        using var anonymous = ProjectWebApplicationFactory.CreateProjectClient(brokenPurgeHost);
        var lookup = await anonymous.GetAsync("/api/v1/tenants/by-name/purge-broken");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);

        // 脏跟踪器里的实体没有被删注册表那一步顺手写进库
        using var scope = brokenPurgeHost.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        Assert.Empty(await db.Set<Role>()
            .IgnoreQueryFilters()
            .Where(r => r.Name == FailingPurgeTenantSeeder.GhostRoleName)
            .ToListAsync());
    }

    /// <summary>
    /// 部分播种后失败：角色已落库、用户尚未写入时补偿，必须连已写入的种子数据一起回滚。
    /// 只软删注册表是不够的——旧租户 Id 下会永久残留角色与授权版本。
    /// </summary>
    [Fact]
    public async Task 部分播种后失败_租户与已写入的种子数据一并回滚()
    {
        // 装饰真实种子：先让它写完角色与权限授予，再抛错——覆盖"部分落库"这条真实路径
        using var brokenHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // 桩把 Purge 委托给真实实现，因此按具体类型注册它——
                // 若桩注入 ITenantSeeder 会解析到自己，形成循环依赖
                services.AddTransient<TenantSeeder>();
                services.AddTransient<ITenantSeeder, FailAfterRolesTenantSeeder>();
            }));

        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(brokenHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var failed = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "compensated",
            AdminEmail = "admin@compensated.example.com",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        var failedTenantId = FailAfterRolesTenantSeeder.LastTenantId;
        Assert.NotNull(failedTenantId);

        // EF InMemory 不提供真实事务，不用它证明授权记录回滚；
        // 这里只回归补偿后租户主体不可访问。事务原子性要在真实关系型数据库上验收，
        // 属于本项目自己的集成/端到端环境，不由这个用例承担。
        using (var scope = brokenHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using (tenant.Change(failedTenantId.Value))
            {
                Assert.Empty(await db.Set<User>().ToListAsync());
                Assert.Empty(await db.Set<Role>().ToListAsync());
            }
        }

        // 独立补偿作用域不得提交失败现场仍在跟踪的实体。
        using (var scope = brokenHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            Assert.Empty(await db.Set<Role>()
                .IgnoreQueryFilters()
                .Where(r => r.Name == FailAfterRolesTenantSeeder.GhostRoleName)
                .ToListAsync());
        }

        using var anonymous = ProjectWebApplicationFactory.CreateProjectClient(brokenHost);
        var lookup = await anonymous.GetAsync("/api/v1/tenants/by-name/compensated");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);

        // 派生宿主与工厂共享数据库，用健康宿主验证同一注册表可直接重试。
        var hostAdmin2 = await LoginHostAdminAsync();
        var retried = await CreateTenantAsync(hostAdmin2, "compensated");
        Assert.NotEqual(Guid.Empty, retried);
    }

    /// <summary>
    /// 断言指定租户上下文内看不到任何租户化数据。
    /// </summary>
    /// <remarks>
    /// 第一步的类型清单是完整性锁：新增租户化实体（或种子开始写入新实体）时，
    /// 清单断言先失败，提醒同步补偿逻辑与此处断言——避免补偿悄悄漏掉新数据。
    /// </remarks>
    private static async Task AssertNoVisibleTenantDataAsync(
        WebApplicationFactory<Program> host, Guid tenantId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        var multiTenantEntities = db.Model.GetEntityTypes()
            .Where(t => typeof(IMultiTenant).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[]
            {
                nameof(AuthorizationVersionRecord),
#if (ExternalLogin)
                nameof(ExternalLoginConnection),
#endif
                nameof(PermissionGrantRecord),
                nameof(Role),
                nameof(User)
            },
            multiTenantEntities);

        // UserRole 不实现 IMultiTenant，进不了上面那份类型清单——必须单独断言。
        // 否则误删 PurgeAsync 里的关联清理时，本文件的用例一个都不会变红：
        // 用户与角色都已软删，孤儿关联行既不可见也没人查。
        // IgnoreQueryFilters 会同时摘掉软删与租户两个过滤器，所以租户条件要显式写
        var tenantUserIds = await db.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .Select(u => u.Id)
            .ToListAsync();

        if (tenantUserIds.Count > 0)
        {
            var liveLinks = await db.Set<UserRole>()
                .IgnoreQueryFilters()
                .Where(ur => tenantUserIds.Contains(ur.UserId) && !ur.IsDeleted)
                .ToListAsync();
            Assert.Empty(liveLinks);
        }

        using (currentTenant.Change(tenantId))
        {
            Assert.Empty(await db.Set<User>().ToListAsync());
            Assert.Empty(await db.Set<Role>().ToListAsync());
            Assert.Empty(await db.Set<PermissionGrantRecord>().ToListAsync());
            Assert.Empty(await db.Set<AuthorizationVersionRecord>().ToListAsync());
#if (ExternalLogin)
            Assert.Empty(await db.Set<ExternalLoginConnection>().ToListAsync());
#endif
        }
    }

    /// <summary>
    /// 播种期间的控制面竞争：另一个合法宿主管理员在种子还没跑完时动这个租户。
    /// </summary>
    /// <remarks>
    /// 用可阻塞种子把"播种中"这个瞬间拉长到可观测：种子先发出"我到了"信号，
    /// 然后挂住等测试放行。测试在这段时间里以第二个宿主会话发起竞争操作。
    /// 这是唯一能覆盖控制面并发的手法——真实播种是亚秒级的，靠时序碰不到。
    /// </remarks>
    [Fact]
    public async Task 播种期间并发删除租户_不留下孤儿业务数据()
    {
        using var blockingHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddTransient<TenantSeeder>();
                services.AddTransient<ITenantSeeder, BlockingTenantSeeder>();
            }));

        BlockingTenantSeeder.Reset();

        using var creator = await ProjectWebApplicationFactory.LoginAsync(blockingHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var racer = await ProjectWebApplicationFactory.LoginAsync(blockingHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var createTask = creator.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "raced-delete",
            AdminEmail = "admin@raced-delete.example.com",
            AdminPassword = "Tenant@123456"
        });

        // 等种子真的开始（角色已写入、管理员还没写），此刻注册表里已有一个停用租户
        var tenantId = await BlockingTenantSeeder.WaitUntilSeedingAsync();

        var delete = await racer.Client.DeleteAsync($"/api/v1/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        BlockingTenantSeeder.Release();
        var created = await createTask;

        // 激活撞上"租户已不存在"：按普通业务失败回 404。
        // 宿主会话不该因为一个别人删掉的租户被登出——会话恢复只服务租户内会话
        Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);

        // 关键断言：激活在补偿边界内，因此已写入的种子数据被一并清掉，
        // 不会永久残留在一个软删租户 Id 下
        await AssertNoVisibleTenantDataAsync(blockingHost, tenantId);
    }

    /// <summary>
    /// 播种期间并发手动启用：租户里还没有用户，启用必须被拒绝。
    /// </summary>
    [Fact]
    public async Task 播种期间并发手动启用被拒_半成品租户不会被提前暴露()
    {
        using var blockingHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddTransient<TenantSeeder>();
                services.AddTransient<ITenantSeeder, BlockingTenantSeeder>();
            }));

        BlockingTenantSeeder.Reset();

        using var creator = await ProjectWebApplicationFactory.LoginAsync(blockingHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var racer = await ProjectWebApplicationFactory.LoginAsync(blockingHost, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var createTask = creator.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "raced-activate",
            AdminEmail = "admin@raced-activate.example.com",
            AdminPassword = "Tenant@123456"
        });

        var tenantId = await BlockingTenantSeeder.WaitUntilSeedingAsync();

        // 抢先启用：租户内还没有任何用户，被业务规则挡住
        var activate = await racer.Client.PutAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/activation", new { IsActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, activate.StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await ProbeAnonymousRegistrationAsync(blockingHost, tenantId));

        BlockingTenantSeeder.Release();
        var created = await createTask;
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var tenantClient = await LoginTenantAdminAsync(blockingHost, tenantId);
        Assert.Equal(["admin"], await GetUsernamesAsync(tenantClient));
    }

    /// <summary>
    /// 真实种子跑到一半挂住，等测试放行；用来把"播种中"拉长成可观测的窗口。
    /// </summary>
    private sealed class BlockingTenantSeeder(
        ICurrentTenant currentTenant,
        TenantSeeder inner) : ITenantSeeder
    {
        private static TaskCompletionSource<Guid> _seeding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static void Reset()
        {
            _seeding = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>等到种子真的开始执行，返回正在播种的租户 Id。</summary>
        internal static Task<Guid> WaitUntilSeedingAsync() => _seeding.Task;

        /// <summary>放行种子继续执行。</summary>
        internal static void Release() => _release.TrySetResult();

        public async Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
        {
            var tenantId = currentTenant.Id ?? throw new InvalidOperationException("种子必须在租户上下文内执行");

            _seeding.TrySetResult(tenantId);
            await _release.Task;

            await inner.SeedAsync(adminEmail, adminPassword, cancellationToken);
        }

        public Task PurgeAsync(CancellationToken cancellationToken = default)
            => inner.PurgeAsync(cancellationToken);
    }

    /// <summary>
    /// 把远端地址改成公网测试地址（TEST-NET-3），使请求不落在任何受信任代理网段内。
    /// </summary>
    private sealed class UntrustedRemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, continuation) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
                    await continuation();
                });

                next(app);
            };
    }

    /// <summary>
    /// 走完真实种子的角色与权限写入后抛错，制造"部分落库"的失败现场。
    /// </summary>
    private sealed class FailAfterRolesTenantSeeder(
        ICurrentTenant currentTenant,
        MyProjectDbContext dbContext,
        TenantSeeder inner) : ITenantSeeder
    {
        /// <summary>失败瞬间留在跟踪器里、绝不应被补偿写入数据库的实体名。</summary>
        internal const string GhostRoleName = "GhostRoleFromDirtyTracker";

        /// <summary>最近一次失败的租户 Id，供测试断言其数据已被清除。</summary>
        internal static Guid? LastTenantId { get; private set; }

        public async Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
        {
            LastTenantId = currentTenant.Id;

            // 走完真实播种后在业务 UoW 提交前失败，同时覆盖
            // 角色、授权、管理员和关联数据的回滚/补偿。
            await inner.SeedAsync(adminEmail, adminPassword, cancellationToken);

            // 制造"脏跟踪器"：EF 在 SaveChanges 失败后会保留 Added/Modified 实体，
            // 这里用一个未保存的 Add 等价模拟——对补偿的影响完全相同。
            // 若补偿复用这个 DbContext，它的下一次 SaveChanges 会把这个实体一起写进库
            dbContext.Add(new Role(GhostRoleName, "Ghost Role"));

            throw new InvalidOperationException("injected seed failure before business unit-of-work commit");
        }

        public Task PurgeAsync(CancellationToken cancellationToken = default)
            => inner.PurgeAsync(cancellationToken);
    }

    /// <summary>
    /// 在真实种子开始之前探测租户此刻是否已对外可用，然后照常完成种子。
    /// </summary>
    private sealed class ProbingTenantSeeder(
        ICurrentTenant currentTenant,
        TenantSeeder inner) : ITenantSeeder
    {
        /// <summary>由测试注入的探测动作（需要测试宿主的 HttpClient，桩自己造不出来）。</summary>
        internal static Func<Guid, Task<HttpStatusCode>>? Probe { get; set; }

        /// <summary>种子中途探测到的状态码。</summary>
        internal static HttpStatusCode? ProbedStatus { get; private set; }

        internal static void Reset()
        {
            Probe = null;
            ProbedStatus = null;
        }

        public async Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
        {
            var tenantId = currentTenant.Id ?? throw new InvalidOperationException("种子必须在租户上下文内执行");

            if (Probe is not null)
            {
                // 探测请求走另一个 HttpClient，与本请求的租户上下文无关——它只带 X-Tenant-Id
                ProbedStatus = await Probe(tenantId);
            }

            await inner.SeedAsync(adminEmail, adminPassword, cancellationToken);
        }

        public Task PurgeAsync(CancellationToken cancellationToken = default)
            => inner.PurgeAsync(cancellationToken);
    }

    private sealed class TenantCreationUnitOfWorkProbe
    {
        private readonly List<(string Phase, Guid UnitOfWorkId)> _observations = [];

        public IReadOnlyList<(string Phase, Guid UnitOfWorkId)> Observations => _observations;

        public void Record(string phase, IUnitOfWorkManager unitOfWorkManager)
        {
            var unitOfWork = unitOfWorkManager.Current
                ?? throw new InvalidOperationException($"Tenant creation phase '{phase}' has no active unit of work.");
            _observations.Add((phase, unitOfWork.Id));
        }
    }

    private sealed class RecordingTenantSeeder(
        TenantSeeder inner,
        IUnitOfWorkManager unitOfWorkManager,
        TenantCreationUnitOfWorkProbe probe) : ITenantSeeder
    {
        public Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
        {
            probe.Record("business", unitOfWorkManager);
            return inner.SeedAsync(adminEmail, adminPassword, cancellationToken);
        }

        public Task PurgeAsync(CancellationToken cancellationToken = default)
            => inner.PurgeAsync(cancellationToken);
    }

    private sealed class RecordingTenantManager(
        EfCoreTenantManager<IdentityControlDbContext> inner,
        IUnitOfWorkManager unitOfWorkManager,
        TenantCreationUnitOfWorkProbe probe) : ITenantManager
    {
        public Task<TenantConfiguration> CreateAsync(
            string name,
            string? displayName,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            probe.Record("control", unitOfWorkManager);
            return inner.CreateAsync(name, displayName, isActive, cancellationToken);
        }

        public Task<TenantConfiguration> SetActiveAsync(
            Guid id,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            if (isActive)
            {
                probe.Record("activation", unitOfWorkManager);
            }

            return inner.SetActiveAsync(id, isActive, cancellationToken);
        }

        public Task<TenantConfiguration> UpdateAsync(
            Guid id,
            string name,
            string? displayName,
            CancellationToken cancellationToken = default)
            => inner.UpdateAsync(id, name, displayName, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(id, cancellationToken);

        public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.FindAsync(id, cancellationToken);

        public Task<TenantPage> GetPagedAsync(
            string? keyword,
            int offset,
            int limit,
            CancellationToken cancellationToken = default)
            => inner.GetPagedAsync(keyword, offset, limit, cancellationToken);
    }

    /// <summary>
    /// 种子失败、且清种子也失败并在自己的作用域里留下脏跟踪器。
    /// </summary>
    private sealed class FailingPurgeTenantSeeder(MyProjectDbContext dbContext) : ITenantSeeder
    {
        /// <summary>清种子失败瞬间留在跟踪器里、绝不应被删注册表那一步写入的实体名。</summary>
        internal const string GhostRoleName = "GhostRoleFromFailedPurge";

        internal static bool PurgeAttempted { get; private set; }

        internal static void Reset() => PurgeAttempted = false;

        public Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("injected seed failure");

        public Task PurgeAsync(CancellationToken cancellationToken = default)
        {
            PurgeAttempted = true;

            // 脏跟踪器 + 抛错：与"SaveChanges 失败后 EF 保留 Added 实体"等价。
            // 共用作用域时，删注册表的 SaveChanges 会把这一行一起写出去
            dbContext.Add(new Role(GhostRoleName, "Ghost Role"));
            throw new InvalidOperationException("injected purge failure");
        }
    }

    /// <summary>
    /// 种子正常完成，但最后一步激活失败。
    /// </summary>
    private sealed class FailActivationTenantManager(EfCoreTenantManager<IdentityControlDbContext> inner) : ITenantManager
    {
        internal static Guid? LastCreatedId { get; private set; }

        internal static void Reset() => LastCreatedId = null;

        public async Task<TenantConfiguration> CreateAsync(
            string name, string? displayName, bool isActive, CancellationToken cancellationToken = default)
        {
            var created = await inner.CreateAsync(name, displayName, isActive, cancellationToken);
            LastCreatedId = created.Id;
            return created;
        }

        public Task<TenantConfiguration> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
            => isActive
                ? throw new InvalidOperationException("injected activation failure")
                : inner.SetActiveAsync(id, isActive, cancellationToken);

        public Task<TenantConfiguration> UpdateAsync(Guid id, string name, string? displayName, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(id, name, displayName, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(id, cancellationToken);

        public Task<TenantConfiguration?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.FindAsync(id, cancellationToken);

        public Task<TenantPage> GetPagedAsync(string? keyword, int offset, int limit, CancellationToken cancellationToken = default)
            => inner.GetPagedAsync(keyword, offset, limit, cancellationToken);
    }

#if (ExternalLogin)
    [Fact]
    public async Task 同一外部身份可在不同租户各自绑定_且查询按租户分区()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantAId = await CreateTenantAsync(hostAdmin, "extlogin-a");
        var tenantBId = await CreateTenantAsync(hostAdmin, "extlogin-b");

        // 外部身份的 (Provider, ProviderUserId) 由第三方决定，只在租户内唯一：
        // 同一个 GitHub 账号必须能在两个租户各自绑定，且各租户只看得见自己的连接
        const string provider = "github";
        const string providerUserId = "gh-42";

        var connectionAId = await BindExternalLoginAsync(tenantAId, provider, providerUserId);
        var connectionBId = await BindExternalLoginAsync(tenantBId, provider, providerUserId);
        Assert.NotEqual(connectionAId, connectionBId);

        Assert.Equal(connectionAId, await FindExternalLoginAsync(tenantAId, provider, providerUserId));
        Assert.Equal(connectionBId, await FindExternalLoginAsync(tenantBId, provider, providerUserId));

        Assert.Null(await FindExternalLoginAsync(tenantId: null, provider, providerUserId));
    }

    /// <summary>在指定租户上下文内为其管理员绑定一个外部身份，返回连接 Id。</summary>
    private async Task<Guid> BindExternalLoginAsync(Guid tenantId, string provider, string providerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var connections = scope.ServiceProvider.GetRequiredService<IRepository<ExternalLoginConnection, Guid>>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        using (currentTenant.Change(tenantId))
        {
            var admin = await users.GetFirstAsync(u => u.Username == "admin", q => q.OrderBy(u => u.Id));
            Assert.NotNull(admin);

            // TenantId 由多租户落值拦截器按当前上下文填充，业务代码不手写
            var connection = new ExternalLoginConnection(admin.Id, provider, providerUserId, clock.Now);
            await connections.InsertAsync(connection);
            return connection.Id;
        }
    }

    /// <summary>在指定租户上下文（null 为宿主）内按外部身份查找连接，返回连接 Id 或 null。</summary>
    private async Task<Guid?> FindExternalLoginAsync(Guid? tenantId, string provider, string providerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var connections = scope.ServiceProvider.GetRequiredService<IRepository<ExternalLoginConnection, Guid>>();

        using (currentTenant.Change(tenantId))
        {
            var found = await connections.GetFirstAsync(
                c => c.Provider == provider && c.ProviderUserId == providerUserId,
                q => q.OrderBy(c => c.Id));
            return found?.Id;
        }
    }

#endif
    [Fact]
    public async Task 删除租户后_名称可复用且旧租户不可达()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "recycled");

        var delete = await hostAdmin.Client.DeleteAsync($"/api/v1/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var stale = _factory.CreateProjectClient();
        stale.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        var staleRequest = await stale.GetAsync("/api/v1/auth/security-config");
        Assert.Equal(HttpStatusCode.NotFound, staleRequest.StatusCode);

        var recreated = await CreateTenantAsync(hostAdmin, "recycled");
        Assert.NotEqual(tenantId, recreated);
    }
}
