#if (TenancyEnabled)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Tenants;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Authorization;
using Leistd.Authorization.EntityFrameworkCore;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
#if (IncludeExternalLogin)
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
        var session = await _factory.LoginAsync("admin", "Admin@123456");
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
    private async Task<HttpClient> LoginTenantAdminAsync(Guid tenantId, string password = "Tenant@123456")
    {
        var client = _factory.CreateProjectClient();
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

    private static async Task<List<string>> GetUsernamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/users?offset=0&limit=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("username").GetString()!)
            .ToList();
    }

    [Fact]
    public async Task 建租户即种子_租户管理员可登录并只见本租户用户()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "acme");

        // 匿名探测：登录页租户选择的数据源
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

        // 宿主管理员列表：不含租户用户
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

        // 取租户 B 管理员的用户 Id
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

        // 租户超管的 current 权限集与检查器同口径：宿主侧权限不下发，
        // 前端菜单/路由据此裁剪，不会出现点进去必然 403 的宿主功能
        var current = await tenantClient.GetAsync("/api/v1/permissions/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var body = await current.Content.ReadAsStringAsync();
        Assert.DoesNotContain("App.Tenants", body);
        Assert.Contains("App.Users", body);

        // 宿主超管则相反：宿主侧权限在集合内
        var hostCurrent = await hostAdmin.Client.GetAsync("/api/v1/permissions/current");
        Assert.Contains("App.Tenants", await hostCurrent.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 租户管理员不能访问租户管理API_宿主侧权限对租户拒绝()
    {
        var hostAdmin = await LoginHostAdminAsync();
        var tenantId = await CreateTenantAsync(hostAdmin, "boundary");

        var tenantClient = await LoginTenantAdminAsync(tenantId);

        // 租户超管也过不了宿主侧权限的侧别硬边界
        var list = await tenantClient.GetAsync("/api/v1/tenants?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var create = await tenantClient.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "evil",
            AdminEmail = "evil@example.com",
            AdminPassword = "Evil@123456"
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

        // 停用：管理器写入即失效存储缓存，在途会话的下一个请求就被拒
        var deactivate = await hostAdmin.Client.PutAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/activation", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // XHR/API 请求：会话自恢复中间件注销 Cookie 并回 401（前端按会话失效清理本地状态）
        var afterDeactivation = await tenantClient.GetAsync("/api/v1/users?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDeactivation.StatusCode);

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

        // 再登录同样被拒（登录请求也在租户校验之后）
        using var relogin = _factory.CreateProjectClient();
        relogin.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        var loginResponse = await relogin.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = "admin", Password = "Tenant@123456" });
        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
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

        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(brokenHost, "admin", "Admin@123456");

        var failed = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "compensated",
            AdminEmail = "admin@compensated.example.com",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        var failedTenantId = FailAfterRolesTenantSeeder.LastTenantId;
        Assert.NotNull(failedTenantId);

        // 补偿覆盖种子数据：在失败租户的上下文里，所有租户化实体都不可见
        await AssertNoVisibleTenantDataAsync(failedTenantId.Value);

        // 补偿在独立作用域里执行，因此不会把失败现场跟踪器里的实体一起提交。
        // 复用失败现场的 DbContext 时，补偿的 SaveChanges 会写出这一行
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            Assert.Empty(await db.Set<Role>()
                .IgnoreQueryFilters()
                .Where(r => r.Name == FailAfterRolesTenantSeeder.GhostRoleName)
                .ToListAsync());
        }

        // 注册表也已回滚，没有留下无管理员的半成品租户
        using var anonymous = _factory.CreateProjectClient();
        var lookup = await anonymous.GetAsync("/api/v1/tenants/by-name/compensated");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);

        // 名称立即可重用：重试不撞名，无需人工清理
        var hostAdmin2 = await LoginHostAdminAsync();
        var retried = await CreateTenantAsync(hostAdmin2, "compensated");
        Assert.NotEqual(Guid.Empty, retried);
    }

    /// <summary>
    /// 断言指定租户上下文内看不到任何租户化数据。
    /// </summary>
    /// <remarks>
    /// 第一步的类型清单是**完整性锁**：新增租户化实体（或种子开始写入新实体）时，
    /// 清单断言先失败，提醒同步补偿逻辑与此处断言——避免补偿悄悄漏掉新数据。
    /// </remarks>
    private async Task AssertNoVisibleTenantDataAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
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
                nameof(AuthorizationRevisionRecord),
#if (IncludeExternalLogin)
                nameof(ExternalLoginConnection),
#endif
                nameof(PermissionGrantRecord),
                nameof(Role),
                nameof(User)
            },
            multiTenantEntities);

        using (currentTenant.Change(tenantId))
        {
            Assert.Empty(await db.Set<User>().ToListAsync());
            Assert.Empty(await db.Set<Role>().ToListAsync());
            Assert.Empty(await db.Set<PermissionGrantRecord>().ToListAsync());
            Assert.Empty(await db.Set<AuthorizationRevisionRecord>().ToListAsync());
#if (IncludeExternalLogin)
            Assert.Empty(await db.Set<ExternalLoginConnection>().ToListAsync());
#endif
        }
    }

    /// <summary>
    /// 走完真实种子的角色与权限写入后抛错，制造"部分落库"的失败现场。
    /// </summary>
    private sealed class FailAfterRolesTenantSeeder(
        ICurrentTenant currentTenant,
        MyProjectDbContext dbContext,
        IRepository<Role, Guid> roleRepository,
        IPermissionDefinitionManager permissionDefinitionManager,
        IPermissionGrantStore permissionGrantStore,
        IPermissionGrantManager permissionGrantManager,
        TenantSeeder inner) : ITenantSeeder
    {
        /// <summary>失败瞬间留在跟踪器里、绝不应被补偿写入数据库的实体名。</summary>
        internal const string GhostRoleName = "GhostRoleFromDirtyTracker";

        /// <summary>最近一次失败的租户 Id，供测试断言其数据已被清除。</summary>
        internal static Guid? LastTenantId { get; private set; }

        public async Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
        {
            LastTenantId = currentTenant.Id;

            // 第一步：真实写入角色（仓储在无工作单元时立即保存，因此这一步确实落库）
            var adminRole = new Role("Admin", "Administrator", isStatic: true, sort: 1);
            await roleRepository.InsertAsync(adminRole, cancellationToken);

            // 第二步：真实写入权限授予与授权版本
            var existing = await permissionGrantStore.GetGrantsAsync(
                PermissionGrantProviderNames.Role, adminRole.Id.ToString(), cancellationToken);
            var definitions = permissionDefinitionManager
                .GetAll()
                .Where(d => d.Side.HasFlag(MultiTenancySides.Tenant))
                .Where(d => permissionDefinitionManager.IsEffectivelyEnabled(d.Name))
                .Select(d => d.Name)
                .ToList();
            await permissionGrantManager.ReplaceGrantsAsync(
                PermissionGrantProviderNames.Role, adminRole.Id.ToString(), definitions,
                expectedRevision: existing.Revision, cancellationToken);

            // 制造"脏跟踪器"：EF 在 SaveChanges 失败后会保留 Added/Modified 实体，
            // 这里用一个未保存的 Add 等价模拟——对补偿的影响完全相同。
            // 若补偿复用这个 DbContext，它的下一次 SaveChanges 会把这个实体一起写进库
            dbContext.Add(new Role(GhostRoleName, "Ghost Role"));

            // 第三步（创建管理员）之前失败
            throw new InvalidOperationException("injected seed failure after roles and grants");
        }

        public Task PurgeAsync(CancellationToken cancellationToken = default)
            => inner.PurgeAsync(cancellationToken);
    }

#if (IncludeExternalLogin)
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

        // 查询分区：租户 A 的按 Provider+ProviderUserId 查找命中自己的连接，而非 B 的
        Assert.Equal(connectionAId, await FindExternalLoginAsync(tenantAId, provider, providerUserId));
        Assert.Equal(connectionBId, await FindExternalLoginAsync(tenantBId, provider, providerUserId));

        // 宿主视角看不到任何租户的连接
        Assert.Null(await FindExternalLoginAsync(tenantId: null, provider, providerUserId));
    }

    /// <summary>在指定租户上下文内为其管理员绑定一个外部身份，返回连接 Id。</summary>
    private async Task<Guid> BindExternalLoginAsync(Guid tenantId, string provider, string providerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var connections = scope.ServiceProvider.GetRequiredService<IRepository<ExternalLoginConnection, Guid>>();

        using (currentTenant.Change(tenantId))
        {
            var admin = await users.GetFirstAsync(u => u.Username == "admin", q => q.OrderBy(u => u.Id));
            Assert.NotNull(admin);

            // TenantId 由多租户落值拦截器按当前上下文填充，业务代码不手写
            var connection = new ExternalLoginConnection(admin.Id, provider, providerUserId);
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

        // 旧租户请求：软删后不可达（404）
        using var stale = _factory.CreateProjectClient();
        stale.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        var staleRequest = await stale.GetAsync("/api/v1/auth/security-config");
        Assert.Equal(HttpStatusCode.NotFound, staleRequest.StatusCode);

        // 名称可复用（管理器在未删除行内校验唯一）
        var recreated = await CreateTenantAsync(hostAdmin, "recycled");
        Assert.NotEqual(tenantId, recreated);
    }
}
#endif
