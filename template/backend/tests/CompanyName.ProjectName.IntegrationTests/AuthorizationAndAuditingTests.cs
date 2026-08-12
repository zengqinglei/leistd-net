#if (IncludeIdentity)
using System.Net;
using System.Net.Http.Json;
#if (IncludeOpenIddict)
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
#endif
#if (IncludeRoles)
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.Dtos;
#endif
#if (IncludeOpenIddict)
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
#endif
using CompanyName.ProjectName.Application.Users.Dtos;
#if (IncludeRoles)
using CompanyName.ProjectName.Domain.Users.Constants;
#endif
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
#if (IncludeRoles)
using Leistd.Authorization.EntityFrameworkCore;
#endif
#if (IncludeRoles)
using Leistd.Authorization;
#endif
using Microsoft.EntityFrameworkCore;
using Leistd.Lock.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class AuthorizationAndAuditingTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
#if (IncludeRoles)
    [Fact]
    public async Task Permissions_should_distinguish_users_roles_and_super_admin()
    {
        using var anonymous = factory.CreateProjectClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/users?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");
        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(superAdmin.Client)).StatusCode);

        var member = await CreateUserAsync(superAdmin.Client);
        using var memberSession = await factory.LoginAsync(member.Username, TestPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetUsersAsync(memberSession.Client)).StatusCode);

        // 用户直授生效。
        await GrantAsync(PermissionGrantProviderNames.User, member.Id, PermissionConstant.Users.Default);
        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(memberSession.Client)).StatusCode);

        // 角色继承生效：新角色初始无权限，授予后其成员立即获得。
        var role = await CreateRoleAsync(superAdmin.Client);
        var roleUser = await CreateUserAsync(superAdmin.Client, [role.Id]);
        using var roleSession = await factory.LoginAsync(roleUser.Username, TestPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetUsersAsync(roleSession.Client)).StatusCode);

        await GrantAsync(PermissionGrantProviderNames.Role, role.Id, PermissionConstant.Users.Default);
        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(roleSession.Client)).StatusCode);
    }

    [Fact]
    public async Task Admin_role_is_seeded_with_every_permission_and_has_no_code_level_bypass()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var adminRoleId = await GetRoleIdAsync(AdminConstant.RoleName);
        var adminRoleUser = await CreateUserAsync(superAdmin.Client, [adminRoleId]);
        using var adminSession = await factory.LoginAsync(adminRoleUser.Username, TestPassword);

        // 初始化已把全部定义授予 Admin 角色，因此其成员立即可用。
        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(adminSession.Client)).StatusCode);

        // 该角色是普通角色：撤销授予后立即失效，不存在"代码里自动全权"的旁路。
        await ReplaceGrantsAsync(PermissionGrantProviderNames.Role, adminRoleId, []);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetUsersAsync(adminSession.Client)).StatusCode);

        // 恢复，避免影响同一 fixture 中的其他用例。
        await GrantAllAsync(adminRoleId);
    }


    [Fact]
    public async Task Update_permission_alone_cannot_change_roles()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var operatorUser = await CreateUserAsync(superAdmin.Client);
        await GrantAsync(
            PermissionGrantProviderNames.User,
            operatorUser.Id,
            PermissionConstant.Users.Default,
            PermissionConstant.Users.Update,
            PermissionConstant.Users.Create);

        using var session = await factory.LoginAsync(operatorUser.Username, TestPassword);
        var target = await CreateUserAsync(superAdmin.Client);
        var adminRoleId = await GetRoleIdAsync(AdminConstant.RoleName);

        // 普通更新已经不再接受角色字段，角色分配端点需要 ManageRoles。
        var updateResponse = await session.Client.PutAsJsonAsync(
            $"/api/v1/users/{target.Id}",
            new UpdateUserInputDto
            {
                Email = target.Email,
                DisplayName = "renamed by operator"
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var assignResponse = await session.Client.PutAsJsonAsync(
            $"/api/v1/users/{target.Id}/roles",
            new UpdateUserRolesInputDto { RoleIds = [adminRoleId] });
        Assert.Equal(HttpStatusCode.Forbidden, assignResponse.StatusCode);

        // 创建时携带角色同样受 ManageRoles 保护，否则只需创建权限即可造出管理员。
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var createResponse = await session.Client.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserInputDto
            {
                Username = $"user_{suffix}",
                Email = $"user_{suffix}@example.test",
                Password = TestPassword,
                IsActive = true,
                RoleIds = [adminRoleId]
            });
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    [Fact]
    public async Task ManageRoles_permission_alone_cannot_update_the_profile()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var roleManager = await CreateUserAsync(superAdmin.Client);
        await GrantAsync(
            PermissionGrantProviderNames.User,
            roleManager.Id,
            PermissionConstant.Users.ManageRoles);

        using var session = await factory.LoginAsync(roleManager.Username, TestPassword);
        var target = await CreateUserAsync(superAdmin.Client);
        var role = await CreateRoleAsync(superAdmin.Client);

        var assignResponse = await session.Client.PutAsJsonAsync(
            $"/api/v1/users/{target.Id}/roles",
            new UpdateUserRolesInputDto { RoleIds = [role.Id] });
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        var updateResponse = await session.Client.PutAsJsonAsync(
            $"/api/v1/users/{target.Id}",
            new UpdateUserInputDto
            {
                Email = target.Email,
                DisplayName = "should not be allowed"
            });
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Granting_a_child_permission_completes_its_parent()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");
        var role = await CreateRoleAsync(superAdmin.Client);

        await ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            role.Id,
            [PermissionConstant.Users.Create]);

        var response = await superAdmin.Client.GetAsync($"/api/v1/permissions/grants/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var grants = await response.Content.ReadFromJsonAsync<PermissionGrantsResponse>();
        Assert.NotNull(grants);

        var parent = grants.Grants.Single(x => x.Name == PermissionConstant.Users.Default);
        Assert.True(parent.Granted);
    }


    [Fact]
    public async Task Disabling_a_user_revokes_their_existing_session()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        await GrantAsync(PermissionGrantProviderNames.User, user.Id, PermissionConstant.Users.Default);

        using var session = await factory.LoginAsync(user.Username, TestPassword);
        Assert.Equal(
            HttpStatusCode.OK,
            (await session.Client.GetAsync("/api/v1/users?offset=0&limit=10")).StatusCode);

        var disabled = await superAdmin.Client.PatchAsync($"/api/v1/users/{user.Id}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        // 登录时会拒绝禁用账号，但已签发的 Cookie 不会因此失效——放行就等于"禁用用户"
        // 只挡新登录，已在线的会话照常畅通。
        // 401 而非 403：账号失效说明这份凭据代表的身份已经不成立，属于"凭据无效"而非"权限不足"，
        // 前端也只把 401 当会话失效来清理登录态。
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await session.Client.GetAsync("/api/v1/users?offset=0&limit=10")).StatusCode);
    }

#if (IncludeOpenIddict)
    [Fact]
    public async Task Creating_an_application_rejects_scopes_this_server_does_not_register()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        // 客户端能请求一个服务端根本没注册的 scope 时，配置存得下但发令牌时必然被拒——
        // 界面裁掉了选项不代表接口就该收下，写入时报错才不会留一个"配置得上、用不了"的客户端。
        var response = await superAdmin.Client.PostAsJsonAsync(
            "/api/v1/open-applications",
            new
            {
                clientId = $"client-{Guid.CreateVersion7():N}",
                displayName = "Probe",
                applicationType = "web",
                clientType = "confidential",
                consentType = "explicit",
                permissions = new[] { "scp:openid", "scp:not_registered" },
                requirements = Array.Empty<string>(),
                redirectUris = Array.Empty<string>(),
                postLogoutRedirectUris = Array.Empty<string>()
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Open_application_endpoints_require_their_own_permissions()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(user.Username, TestPassword);

        // 已认证不等于可管理 OAuth 客户端：开放应用持有可对外颁发令牌的凭据。
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await session.Client.GetAsync("/api/v1/open-applications?offset=0&limit=10")).StatusCode);

        await GrantAsync(
            PermissionGrantProviderNames.User,
            user.Id,
            PermissionConstant.OpenApplications.Default);
        Assert.Equal(
            HttpStatusCode.OK,
            (await session.Client.GetAsync("/api/v1/open-applications?offset=0&limit=10")).StatusCode);

        // 查看权限不含写入：创建仍被拒绝。
        var create = await session.Client.PostAsJsonAsync(
            "/api/v1/open-applications",
            new
            {
                clientId = $"client-{Guid.CreateVersion7():N}",
                displayName = "Probe",
                applicationType = "web",
                clientType = "confidential"
            });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

#endif
    [Fact]
    public async Task Undefined_permissions_surface_as_bad_request_not_server_error()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");
        var role = await CreateRoleAsync(superAdmin.Client);

        // 未定义权限由授予管理器统一拒绝；应用层只做翻译，不重复判断。
        // 若这条异常没有被翻译，全局处理器会把它归一化成 500——这正是要锁住的行为。
        var response = await superAdmin.Client.PutAsJsonAsync(
            $"/api/v1/permissions/grants/roles/{role.Id}",
            new
            {
                expectedRevision = 0,
                permissionNames = new[] { "App.NotDefined" }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Managing_permissions_implies_reading_the_permission_definitions()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var operatorUser = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(operatorUser.Username, TestPassword);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await session.Client.GetAsync("/api/v1/permissions/definitions")).StatusCode);

        // 只授予「配置角色权限」，不授予 App.Permissions：定义树是配置权限的前置条件，
        // 要求额外记得授一个根权限只会制造「有权限却打不开界面」的无用状态。
        await GrantAsync(
            PermissionGrantProviderNames.User,
            operatorUser.Id,
            PermissionConstant.Roles.ManagePermissions);

        Assert.Equal(
            HttpStatusCode.OK,
            (await session.Client.GetAsync("/api/v1/permissions/definitions")).StatusCode);
    }


    [Fact]
    public async Task Revoking_an_admin_permission_survives_re_initialization()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var adminRole = await GetRoleByNameAsync(AdminConstant.RoleName);
        var grants = await superAdmin.Client
            .GetFromJsonAsync<PermissionGrantsResponse>($"/api/v1/permissions/grants/roles/{adminRole.Id}");
        Assert.NotNull(grants);

        // 撤掉一条叶子权限（撤父级会连带清空子孙，不便于观察）。
        var kept = grants.Grants
            .Where(x => x.Granted && x.Name != PermissionConstant.Users.Delete)
            .Select(x => x.Name)
            .ToArray();
        Assert.DoesNotContain(PermissionConstant.Users.Delete, kept);

        var revoke = await superAdmin.Client.PutAsJsonAsync(
            $"/api/v1/permissions/grants/roles/{adminRole.Id}",
            new { expectedRevision = grants.Revision, permissionNames = kept });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        // 再跑一次初始化：Admin 是普通角色，撤权就该一直是撤掉的状态。
        // 启动时"补齐缺失权限"看着无害，实际会把人工撤权原样加回来，"可撤权"就成了空话。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISystemInitializer>().InitializeAsync();
        }

        var after = await superAdmin.Client
            .GetFromJsonAsync<PermissionGrantsResponse>($"/api/v1/permissions/grants/roles/{adminRole.Id}");
        Assert.NotNull(after);
        Assert.False(after.Grants.Single(x => x.Name == PermissionConstant.Users.Delete).Granted);
        Assert.True(after.Grants.Single(x => x.Name == PermissionConstant.Users.Default).Granted);
    }

    [Fact]
    public async Task Admin_permission_seeding_recovers_when_a_previous_run_was_interrupted()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");
        var adminRole = await GetRoleByNameAsync(AdminConstant.RoleName);

        // 复刻"角色已建、权限未播"：初始化没有事务，角色是立即落库的，这一状态确实可达。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var providerKey = adminRole.Id.ToString();

            dbContext.RemoveRange(await dbContext.Set<PermissionGrantRecord>()
                .Where(x => x.ProviderName == PermissionGrantProviderNames.Role && x.ProviderKey == providerKey)
                .ToListAsync());
            dbContext.RemoveRange(await dbContext.Set<AuthorizationRevisionRecord>()
                .Where(x => x.ProviderName == PermissionGrantProviderNames.Role && x.ProviderKey == providerKey)
                .ToListAsync());
            await dbContext.SaveChangesAsync();
        }

        var interrupted = await superAdmin.Client
            .GetFromJsonAsync<PermissionGrantsResponse>($"/api/v1/permissions/grants/roles/{adminRole.Id}");
        Assert.NotNull(interrupted);
        Assert.Equal(0, interrupted.Revision);
        Assert.DoesNotContain(interrupted.Grants, x => x.Granted);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISystemInitializer>().InitializeAsync();
        }

        // 判据是"授权版本为 0"而不是"本次新建了角色"：只认后者的话，那一次中断会让 Admin 永久缺权限。
        var recovered = await superAdmin.Client
            .GetFromJsonAsync<PermissionGrantsResponse>($"/api/v1/permissions/grants/roles/{adminRole.Id}");
        Assert.NotNull(recovered);
        Assert.All(recovered.Grants, grant => Assert.True(grant.Granted));
    }

    [Fact]
    public async Task Concurrent_initializers_are_serialized_by_the_initialization_lock()
    {
        // 断言的是"初始化确实在锁内执行"，不是"并发时会崩"：测试宿主是单进程 + InMemory
        // Provider，InMemory 不强制唯一索引，多实例真正的失败形态在这里复现不出来，
        // 写成"不抛异常即通过"的用例无论有没有锁都会绿，等于没测。
        // 跨进程互斥由部署侧保证——多副本必须配置 Redis，那条路径不在集成测试范围内。
        //
        // 探针替换 IDistributedLock 而不是在初始化之后另取一把锁：后者两个任务本来就会被
        // 那把锁串行，与初始化有没有加锁无关，测不出任何东西。
        var probe = new LockUsageProbe();

        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDistributedLock>();
            services.AddSingleton<IDistributedLock>(probe);
        }));

        using var superAdmin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", "Admin@123456");
        var adminRole = await GetRoleByNameAsync(AdminConstant.RoleName);

        // 复刻多实例同时启动：清掉授予与版本行，让两个 initializer 都看到"尚未播种"。
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var providerKey = adminRole.Id.ToString();

            dbContext.RemoveRange(await dbContext.Set<PermissionGrantRecord>()
                .Where(x => x.ProviderName == PermissionGrantProviderNames.Role && x.ProviderKey == providerKey)
                .ToListAsync());
            dbContext.RemoveRange(await dbContext.Set<AuthorizationRevisionRecord>()
                .Where(x => x.ProviderName == PermissionGrantProviderNames.Role && x.ProviderKey == providerKey)
                .ToListAsync());
            await dbContext.SaveChangesAsync();
        }

        using var barrier = new Barrier(2);

        async Task RunInitializerAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var initializer = scope.ServiceProvider.GetRequiredService<ISystemInitializer>();
            barrier.SignalAndWait();
            await initializer.InitializeAsync();
        }

        // 宿主自身的 ApplicationBootstrapper 启动时已经初始化过一次，取增量而不是绝对值。
        var acquiredBefore = probe.AcquireCount;

        await Task.WhenAll(Task.Run(RunInitializerAsync), Task.Run(RunInitializerAsync));

        Assert.Equal(2, probe.AcquireCount - acquiredBefore);
        Assert.Equal(1, probe.MaxConcurrentHolders);

        // 串行之后第二个实例会看到版本已大于 0 并整体跳过，因此只播种一次。
        var grants = await superAdmin.Client
            .GetFromJsonAsync<PermissionGrantsResponse>($"/api/v1/permissions/grants/roles/{adminRole.Id}");
        Assert.NotNull(grants);
        Assert.All(grants.Grants, grant => Assert.True(grant.Granted));
        Assert.Equal(1, grants.Revision);
    }

    /// <summary>
    /// 记录初始化锁使用情况的替身：本身提供真实互斥，同时把"取过几次""同时几人持有"暴露出来。
    /// </summary>
    private sealed class LockUsageProbe : IDistributedLock
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private int acquireCount;
        private int currentHolders;
        private int maxConcurrentHolders;

        public int AcquireCount => Volatile.Read(ref acquireCount);

        public int MaxConcurrentHolders => Volatile.Read(ref maxConcurrentHolders);

        public async Task<ILockHandle> LockAsync(string key, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);

            if (key == SystemInitializer.InitializationLockKey)
            {
                Interlocked.Increment(ref acquireCount);
                var holders = Interlocked.Increment(ref currentHolders);
                InterlockedMax(ref maxConcurrentHolders, holders);
            }

            return new Handle(this, key);
        }

        public async Task<ILockHandle?> TryLockAsync(
            string key,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => await LockAsync(key, cancellationToken);

        private void Release(string key)
        {
            if (key == SystemInitializer.InitializationLockKey)
            {
                Interlocked.Decrement(ref currentHolders);
            }

            gate.Release();
        }

        private static void InterlockedMax(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current) return;
                current = seen;
            }
        }

        private sealed class Handle(LockUsageProbe owner, string key) : ILockHandle
        {
            /// <summary>探针不模拟租约失效。</summary>
            public CancellationToken LockLost => CancellationToken.None;

            public ValueTask DisposeAsync()
            {
                owner.Release(key);
                return ValueTask.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task Deleting_a_role_removes_its_grants_and_revision()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");
        var role = await CreateRoleAsync(superAdmin.Client);

        var seeded = await superAdmin.Client.PutAsJsonAsync(
            $"/api/v1/permissions/grants/roles/{role.Id}",
            new { expectedRevision = 0, permissionNames = new[] { PermissionConstant.Users.Default } });
        Assert.Equal(HttpStatusCode.OK, seeded.StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await superAdmin.Client.DeleteAsync($"/api/v1/roles/{role.Id}")).StatusCode);

        // 授予与版本必须跟着角色一起消失：主体没了还留着版本行只会变成永久孤儿，
        // 而角色 Id 一旦被重用，新角色还会继承上一任的版本号。
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var providerKey = role.Id.ToString();

        Assert.Empty(await dbContext.Set<PermissionGrantRecord>()
            .Where(x => x.ProviderName == PermissionGrantProviderNames.Role && x.ProviderKey == providerKey)
            .ToListAsync());
        Assert.Empty(await dbContext.Set<AuthorizationRevisionRecord>()
            .Where(x => x.ProviderName == PermissionGrantProviderNames.Role && x.ProviderKey == providerKey)
            .ToListAsync());
    }

    [Fact]
    public async Task Disabled_permissions_are_not_offered_by_the_definitions_endpoint()
    {
        // 有效启用是在定义加载时一次性预计算的，运行时改 IsEnabled 不生效，
        // 因此禁用必须发生在定义阶段——追加一个只在本宿主生效的定义提供器。
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IPermissionDefinitionProvider, DisableUserDeleteProvider>()));

        using var superAdmin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", "Admin@123456");

        var groups = await superAdmin.Client
            .GetFromJsonAsync<List<PermissionDefinitionGroupResponse>>("/api/v1/permissions/definitions");
        Assert.NotNull(groups);

        var names = groups.SelectMany(group => group.Permissions).SelectMany(Flatten).ToList();

        // 启用的父级下面挂着被禁用的子权限时，若照单下发，界面会渲染成可勾选项，
        // 而写入时又会以"未定义或已禁用"拒绝——用户只能拿到一个无从解释的 400。
        Assert.DoesNotContain(PermissionConstant.Users.Delete, names);
        Assert.Contains(PermissionConstant.Users.Default, names);
        Assert.Contains(PermissionConstant.Users.Create, names);

        static IEnumerable<string> Flatten(PermissionDefinitionResponse definition)
            => [definition.Name, .. definition.Children.SelectMany(Flatten)];
    }

    /// <summary>只在上面那条用例的宿主里禁用一个叶子权限，用于验证定义接口不下发禁用项。</summary>
    private sealed class DisableUserDeleteProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            var permission = context.GetPermissionOrNull(PermissionConstant.Users.Delete);
            if (permission != null)
            {
                permission.IsEnabled = false;
            }
        }
    }

    [Fact]
    public async Task Concurrent_permission_saves_return_conflict_instead_of_overwriting()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");
        var role = await CreateRoleAsync(superAdmin.Client);

        var first = await superAdmin.Client.PutAsJsonAsync(
            $"/api/v1/permissions/grants/roles/{role.Id}",
            new
            {
                expectedRevision = 0,
                permissionNames = new[] { PermissionConstant.Users.Default }
            });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 第二个管理员仍持有旧版本，保存必须被拒绝而不是覆盖前一次修改。
        var stale = await superAdmin.Client.PutAsJsonAsync(
            $"/api/v1/permissions/grants/roles/{role.Id}",
            new
            {
                expectedRevision = 0,
                permissionNames = new[] { PermissionConstant.Roles.Default }
            });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await superAdmin.Client
            .GetFromJsonAsync<PermissionGrantsResponse>($"/api/v1/permissions/grants/roles/{role.Id}");
        Assert.NotNull(current);
        Assert.True(current.Grants.Single(x => x.Name == PermissionConstant.Users.Default).Granted);
        Assert.False(current.Grants.Single(x => x.Name == PermissionConstant.Roles.Default).Granted);
    }

    [Fact]
    public async Task Current_permissions_reflect_grants_and_change_revision()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(user.Username, TestPassword);

        var before = await session.Client.GetFromJsonAsync<CurrentPermissionsResponse>("/api/v1/permissions/current");
        Assert.NotNull(before);
        Assert.DoesNotContain(PermissionConstant.Users.Default, before.Permissions);
        Assert.False(before.IsSuperAdmin);

        await GrantAsync(PermissionGrantProviderNames.User, user.Id, PermissionConstant.Users.Default);

        var after = await session.Client.GetFromJsonAsync<CurrentPermissionsResponse>("/api/v1/permissions/current");
        Assert.NotNull(after);
        Assert.Contains(PermissionConstant.Users.Default, after.Permissions);
        Assert.NotEqual(before.Revision, after.Revision);
    }

    [Fact]
    public async Task Static_roles_cannot_be_deleted_and_assigned_roles_are_protected()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var adminRoleId = await GetRoleIdAsync(AdminConstant.RoleName);
        var staticDelete = await superAdmin.Client.DeleteAsync($"/api/v1/roles/{adminRoleId}");
        Assert.Equal(HttpStatusCode.BadRequest, staticDelete.StatusCode);

        var role = await CreateRoleAsync(superAdmin.Client);
        await CreateUserAsync(superAdmin.Client, [role.Id]);

        var assignedDelete = await superAdmin.Client.DeleteAsync($"/api/v1/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, assignedDelete.StatusCode);
    }

    [Fact]
    public async Task Undefined_permissions_are_rejected_instead_of_being_stored()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");
        var role = await CreateRoleAsync(superAdmin.Client);

        var response = await superAdmin.Client.PutAsJsonAsync(
            $"/api/v1/permissions/grants/roles/{role.Id}",
            new
            {
                expectedRevision = 0,
                permissionNames = new[] { "App.NotDefined" }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
#else
    [Fact]
    public async Task Authenticated_users_should_not_require_role_permissions_when_roles_are_disabled()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var member = await CreateUserAsync(admin.Client);
        using var memberSession = await factory.LoginAsync(member.Username, TestPassword);

        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(memberSession.Client)).StatusCode);
    }

#if (IncludeOpenIddict)
    [Fact]
    public async Task Creating_an_application_rejects_the_roles_scope_when_roles_are_trimmed()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        // 裁掉角色能力的项目里根本没有 roles scope，接口不该收下它。
        var response = await superAdmin.Client.PostAsJsonAsync(
            "/api/v1/open-applications",
            new
            {
                clientId = $"client-{Guid.CreateVersion7():N}",
                displayName = "Probe",
                applicationType = "web",
                clientType = "confidential",
                consentType = "explicit",
                permissions = new[] { "scp:openid", "scp:roles" },
                requirements = Array.Empty<string>(),
                redirectUris = Array.Empty<string>(),
                postLogoutRedirectUris = Array.Empty<string>()
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
#endif
#endif

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("*/*")]
    [InlineData("text/html,application/xhtml+xml")]
    [InlineData("text/html;q=0, application/json")]
    public async Task Unauthenticated_api_calls_answer_401_regardless_of_accept(string? accept)
    {
        using var anonymous = factory.CreateProjectClient();
        if (accept != null)
        {
            anonymous.DefaultRequestHeaders.Add("Accept", accept);
        }

        // Cookie 中间件默认把一切未认证请求 302 到登录页/拒绝页；本宿主是 SPA + API，
        // 没有受保护的 SSR 页面需要那条分支，跟随重定向的客户端只会撞上 SPA 兜底拿到 HTML 200，
        // 把"未认证"伪装成成功。也不按 Accept 猜测：text/html;q=0 明确表示不接受 HTML，
        // 任何子串匹配都会把它误判成浏览器导航。
        var response = await anonymous.GetAsync("/api/v1/users?offset=0&limit=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Logging_out_works_even_after_the_account_was_disabled()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(user.Username, TestPassword);

        Assert.Equal(
            HttpStatusCode.OK,
            (await superAdmin.Client.PatchAsync($"/api/v1/users/{user.Id}/disable", null)).StatusCode);

        // 登出是幂等的 Cookie 清理。挂 [Authorize] 时账号一被禁用本人就清不掉服务端 Cookie——
        // 登不出去，浏览器里还留着一张已经没用的票。
        Assert.Equal(
            HttpStatusCode.OK,
            (await session.Client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_also_revokes_endpoints_that_only_require_authentication()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(user.Username, TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await superAdmin.Client.PatchAsync($"/api/v1/users/{user.Id}/disable", null)).StatusCode);

        // 只在权限判定里补检查的话，撤权只覆盖 RBAC 接口，`/auth/me` 这类"仅要求已认证"的端点照常畅通。
        // 这条要求挂在默认策略上，正是为了让这里也失效。
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

#if (IncludeNotifications)
    [Fact]
    public async Task Disabling_a_user_blocks_new_hub_connections()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(user.Username, TestPassword);

        // 打 negotiate 而不是引 SignalR.Client：Hub 端点的授权就发生在这一步，
        // 走的是同一条 RequireAuthorization() → 默认策略的路径，不必为一条测试加包依赖。
        const string Negotiate = "/hubs/notifications/negotiate?negotiateVersion=1";
        Assert.Equal(HttpStatusCode.OK, (await session.Client.PostAsync(Negotiate, null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await superAdmin.Client.PatchAsync($"/api/v1/users/{user.Id}/disable", null)).StatusCode);

        // 注意这条测试锁住的是"新连接建不起来"。SignalR 只在握手阶段授权，
        // 已经建立的连接不会因为账号被禁用而断开——那条边界写在 ActiveUserRequirement 的说明里，
        // 需要它也失效的项目得自己做连接注册表加跨节点终止通道。
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await session.Client.PostAsync(Negotiate, null)).StatusCode);
    }

#endif
    [Fact]
    public async Task Locking_a_user_revokes_their_existing_session()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(user.Username, TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var entity = await dbContext.Set<User>().SingleAsync(x => x.Id == user.Id);
            entity.Lock();
            await dbContext.SaveChangesAsync();
        }

        // 锁定与禁用是同一句判定的两个分支，失效语义必须一致——只测其中一个，
        // 另一个分支写错了没人会发现。
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

#if (IncludeOpenIddict)
    [Fact]
    public async Task Client_credentials_tokens_cannot_reach_user_management()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        using var machine = await CreateMachineClientAsync(
            superAdmin.Client, $"client-{Guid.CreateVersion7():N}");

        // client_credentials 的 sub 是 client_id，代表工作负载而非人。默认策略是管理接口的兜底，
        // 它要表达的是"一个可用的自然人"——放行等于任何机器令牌都能列用户和 OAuth 客户端，
        // 而裁掉角色的项目里这些接口只剩 [Authorize]，没有第二道拦截。
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await machine.GetAsync("/api/v1/users?offset=0&limit=10")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await machine.GetAsync("/api/v1/open-applications?offset=0&limit=10")).StatusCode);
    }

    [Fact]
    public async Task Access_tokens_are_only_accepted_from_the_authorization_header()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        var (clientId, clientSecret) = await CreateAuthorizationCodeClientAsync(superAdmin.Client);

        using var session = await factory.LoginAsync(user.Username, TestPassword);
        var accessToken = await AuthorizeAndExchangeAsync(session, clientId, clientSecret);

        // 同一个有效令牌：走 Authorization 头能认证，走 query 一律不认。
        // 令牌进 URL 就会进网关访问日志、APM、浏览器历史与 Referer，RFC 6750 §2.3 因此写的是
        // "除非无法用 Authorization 头，否则 SHOULD NOT"。真需要（浏览器 WebSocket/SSE 设不了
        // 自定义头）时按路径定向搬运，见 Program.cs 中 AddValidation 处的说明——那是 /hubs/* 的
        // 需要，不是全部 API 的。这条断言锁住这个决定：谁把全局提取重新打开，这里会红。
        using var viaHeader = CreateHttpsClient();
        viaHeader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await viaHeader.GetAsync("/api/v1/auth/me")).StatusCode);

        using var viaQuery = CreateHttpsClient();
        var queryResponse = await viaQuery.GetAsync(
            $"/api/v1/auth/me?access_token={Uri.EscapeDataString(accessToken)}");

        Assert.Equal(HttpStatusCode.Unauthorized, queryResponse.StatusCode);
    }

    [Fact]
    public async Task A_cookie_session_is_not_reported_as_a_bearer_challenge()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(user.Username, TestPassword);

        // Cookie 认证的请求顺带挂一个无关的 Bearer 头——按请求头形态判断的实现会把它
        // 误标成 Bearer challenge。判据必须是"本次请求实际由哪个方案认证成功"。
        session.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await superAdmin.Client.PatchAsync($"/api/v1/users/{user.Id}/disable", null)).StatusCode);

        var revoked = await session.Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Empty(revoked.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task Client_credentials_cannot_impersonate_a_user_by_taking_their_id_as_client_id()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var victim = await CreateUserAsync(superAdmin.Client);
#if (IncludeRoles)
        await GrantAsync(PermissionGrantProviderNames.User, victim.Id, PermissionConstant.Users.Default);
#endif

        // 攻击者挑一个已存在的用户 Id 当 client_id：用户 Id 从用户管理、审计日志或业务数据里
        // 都拿得到，碰撞不是偶然而是被挑出来的。只要机器令牌的 sub 与人类主体共用一个
        // 命名空间，sub 就会被解析成这个用户，机器令牌随之继承他的直授、角色乃至超管身份。
        using var machine = await CreateMachineClientAsync(superAdmin.Client, victim.Id.ToString());

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await machine.GetAsync("/api/v1/users?offset=0&limit=10")).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_revokes_their_already_issued_bearer_token()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var user = await CreateUserAsync(superAdmin.Client);
        var (clientId, clientSecret) = await CreateAuthorizationCodeClientAsync(superAdmin.Client);

        using var session = await factory.LoginAsync(user.Username, TestPassword);
        var accessToken = await AuthorizeAndExchangeAsync(session, clientId, clientSecret);

        using var api = CreateHttpsClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/connect/userinfo")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await superAdmin.Client.PatchAsync($"/api/v1/users/{user.Id}/disable", null)).StatusCode);

        // Cookie、OpenIddict Validation、userinfo 是三条不同的认证路径。撤权承诺覆盖 API 令牌，
        // 不只是浏览器会话——只有 Cookie 一条测试保持绿色时，策略被改窄了也看不出来。
        var revoked = await api.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        // 401 必须带 challenge：RFC 9110 对此是 MUST，OAuth 客户端也据此把响应识别为
        // "令牌失效、去重新取"，而不是当成一个普通业务错误重试到底。
        var challenge = Assert.Single(revoked.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("invalid_token", challenge.Parameter);


        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/connect/userinfo")).StatusCode);
    }

    /// <summary>注册一个 client_credentials 应用，取令牌，返回已带 Bearer 的客户端。</summary>
    private async Task<HttpClient> CreateMachineClientAsync(HttpClient superAdminClient, string clientId)
    {
        var created = await superAdminClient.PostAsJsonAsync(
            "/api/v1/open-applications",
            new
            {
                clientId,
                displayName = "Workload",
                applicationType = "service",
                clientType = "confidential",
                consentType = "explicit",
                permissions = new[] { "ept:token", "gt:client_credentials" },
                requirements = Array.Empty<string>(),
                redirectUris = Array.Empty<string>(),
                postLogoutRedirectUris = Array.Empty<string>()
            });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var application = await created.Content.ReadFromJsonAsync<OpenApplicationOutputDto>();
        Assert.NotNull(application);

        var reset = await superAdminClient.PostAsync(
            $"/api/v1/open-applications/{application.Id}/reset-secret", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var secret = await reset.Content.ReadFromJsonAsync<ResetOpenApplicationSecretOutputDto>();
        Assert.NotNull(secret);

        var machine = CreateHttpsClient();
        var token = await machine.PostAsync("/connect/token", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", secret.ClientSecret)
        ]));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);

        var payload = await token.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(payload);
        machine.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);

        return machine;
    }

    private static async Task<(string ClientId, string ClientSecret)> CreateAuthorizationCodeClientAsync(
        HttpClient superAdminClient)
    {
        var clientId = $"client-{Guid.CreateVersion7():N}";
        var created = await superAdminClient.PostAsJsonAsync(
            "/api/v1/open-applications",
            new
            {
                clientId,
                displayName = "Bearer probe",
                applicationType = "web",
                clientType = "confidential",
                consentType = "explicit",
                permissions = new[]
                {
                    "ept:authorization", "ept:token", "gt:authorization_code", "rst:code", "scp:openid"
                },
                requirements = Array.Empty<string>(),
                redirectUris = new[] { RedirectUri },
                postLogoutRedirectUris = Array.Empty<string>()
            });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var application = await created.Content.ReadFromJsonAsync<OpenApplicationOutputDto>();
        Assert.NotNull(application);

        var reset = await superAdminClient.PostAsync(
            $"/api/v1/open-applications/{application.Id}/reset-secret", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var secret = await reset.Content.ReadFromJsonAsync<ResetOpenApplicationSecretOutputDto>();
        Assert.NotNull(secret);

        return (clientId, secret.ClientSecret);
    }

    /// <summary>用登录态跑一遍授权码流程，换出该用户的 access token。</summary>
    /// <remarks>
    /// 授权端点没有同意页：Cookie 有效就直接 SignIn 并 302 回带 code 的回调地址，
    /// 所以这一段比"引入 password grant"便宜得多，也不必为测试放宽服务端配置。
    /// 服务端启用了 RequireProofKeyForCodeExchange，因此 code_challenge 必须是真的 S256。
    /// </remarks>
    private async Task<string> AuthorizeAndExchangeAsync(
        AuthenticatedSession session, string clientId, string clientSecret)
    {
        var verifier = Guid.CreateVersion7().ToString("N") + Guid.CreateVersion7().ToString("N");
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        // 另起一个客户端而不是改 session.Client：后者已经发过登录请求，BaseAddress 不再可写。
        // 带上同一份 Cookie，授权端点才认得出登录态。
        using var browser = CreateHttpsClient();
        browser.DefaultRequestHeaders.Add("Cookie", session.Cookie);

        var authorize = await browser.GetAsync(
            "/connect/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=code&scope=openid" +
            $"&code_challenge={challenge}&code_challenge_method=S256");

        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);

        var code = authorize.Headers.Location!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "code")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .SingleOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var exchange = CreateHttpsClient();
        var token = await exchange.PostAsync("/connect/token", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code!),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("code_verifier", verifier)
        ]));
        Assert.True(token.IsSuccessStatusCode, await token.Content.ReadAsStringAsync());

        var payload = await token.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(payload);

        return payload.AccessToken;
    }

    private const string RedirectUri = "https://localhost/callback";

    /// <summary>
    /// 基地址为 https 的客户端。
    /// </summary>
    /// <remarks>
    /// OpenIddict 的授权与令牌端点只收 HTTPS。TestServer 不做真实 TLS，改基地址即可让
    /// <c>Request.IsHttps</c> 成立，不必为测试在服务端放宽这条要求——那等于把生产配置改松来迁就测试。
    /// </remarks>
    private HttpClient CreateHttpsClient()
    {
        var client = factory.CreateProjectClient();
        client.BaseAddress = new Uri("https://localhost");

        return client;
    }

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken);
#endif

    [Fact]
    public async Task Audit_fields_should_follow_the_authenticated_operator()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var created = await CreateUserAsync(admin.Client);

        Guid adminId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            adminId = await db.Users.Where(user => user.IsSuperAdmin).Select(user => user.Id).SingleAsync();
            var entity = await db.Users.AsNoTracking().SingleAsync(user => user.Id == created.Id);
            Assert.Equal(adminId.ToString(), entity.CreatorId);
            Assert.NotEqual(default, entity.CreationTime);
        }

        var updateResponse = await admin.Client.PutAsJsonAsync(
            $"/api/v1/users/{created.Id}",
            new UpdateUserInputDto
            {
                Email = created.Email,
                DisplayName = "Audited user updated"
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var entity = await db.Users.AsNoTracking().SingleAsync(user => user.Id == created.Id);
            Assert.Equal(adminId.ToString(), entity.LastModifierId);
            Assert.NotNull(entity.LastModificationTime);
        }

        var deleteResponse = await admin.Client.DeleteAsync($"/api/v1/users/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var entity = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(user => user.Id == created.Id);
            Assert.True(entity.IsDeleted);
            Assert.Equal(adminId.ToString(), entity.DeleterId);
            Assert.NotNull(entity.DeletionTime);
        }
    }

    private const string TestPassword = "Test123456";

    private static Task<HttpResponseMessage> GetUsersAsync(HttpClient client)
        => client.GetAsync("/api/v1/users?offset=0&limit=10");

#if (IncludeRoles)
    private async Task<Guid> GetRoleIdAsync(string roleName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Roles.Where(role => role.Name == roleName).Select(role => role.Id).SingleAsync();
    }

    private async Task GrantAsync(string providerName, Guid providerKey, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
        foreach (var permission in permissions)
        {
            await manager.GrantAsync(permission, providerName, providerKey.ToString());
        }
    }

    private async Task ReplaceGrantsAsync(
        string providerName,
        Guid providerKey,
        IReadOnlyCollection<string> permissionNames)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
        await manager.ReplaceGrantsAsync(providerName, providerKey.ToString(), permissionNames);
    }

    private async Task GrantAllAsync(Guid roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();

        await manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            roleId.ToString(),
            [.. definitions.GetAll().Select(x => x.Name)]);
    }

    private static async Task<RoleOutputDto> CreateRoleAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var response = await client.PostAsJsonAsync(
            "/api/v1/roles",
            new CreateRoleInputDto
            {
                Name = $"role_{suffix}",
                DisplayName = $"Integration role {suffix}"
            });

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Create role failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<RoleOutputDto>())!;
    }

    private async Task<Role> GetRoleByNameAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await dbContext.Set<Role>().AsNoTracking().SingleAsync(role => role.Name == name);
    }

    private sealed record CurrentPermissionsResponse(string[] Permissions, bool IsSuperAdmin, string Revision);

    private sealed record PermissionDefinitionGroupResponse(
        string Name,
        string DisplayName,
        PermissionDefinitionResponse[] Permissions);

    private sealed record PermissionDefinitionResponse(
        string Name,
        string DisplayName,
        string? ParentName,
        PermissionDefinitionResponse[] Children);

    private sealed record PermissionGrantsResponse(long Revision, PermissionGrantStateResponse[] Grants);

    private sealed record PermissionGrantStateResponse(string Name, bool Granted);
#endif

    private static async Task<UserManagementOutputDto> CreateUserAsync(
        HttpClient client
#if (IncludeRoles)
        , List<Guid>? roleIds = null
#endif
        )
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserInputDto
            {
                Username = $"user_{suffix}",
                Email = $"user_{suffix}@example.test",
                DisplayName = "Integration user",
                Password = TestPassword,
                IsActive = true,
#if (IncludeRoles)
                RoleIds = roleIds ?? []
#endif
            });

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Create user failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<UserManagementOutputDto>())!;
    }
}
#endif
