#if (IncludeIdentity)
using System.Net;
using System.Net.Http.Json;
#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.Dtos;
#endif
using CompanyName.ProjectName.Application.Users.Dtos;
#if (IncludeRoles)
using CompanyName.ProjectName.Domain.Users.Constants;
#endif
using CompanyName.ProjectName.Infrastructure.Persistence;
#if (IncludeRoles)
using Leistd.Authorization;
#endif
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.Contains(anonymousResponse.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Redirect });

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
    public async Task Explicit_deny_on_the_user_beats_a_grant_inherited_from_a_role()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var role = await CreateRoleAsync(superAdmin.Client);
        await GrantAsync(PermissionGrantProviderNames.Role, role.Id, PermissionConstant.Users.Default);

        var user = await CreateUserAsync(superAdmin.Client, [role.Id]);
        using var session = await factory.LoginAsync(user.Username, TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(session.Client)).StatusCode);

        await ReplaceGrantsAsync(
            PermissionGrantProviderNames.User,
            user.Id,
            [new PermissionGrant(PermissionConstant.Users.Default, PermissionGrantEffect.Prohibited)]);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetUsersAsync(session.Client)).StatusCode);
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
                DisplayName = "renamed by operator",
                IsActive = true
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
                DisplayName = "should not be allowed",
                IsActive = true
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
            [new PermissionGrant(PermissionConstant.Users.Create, PermissionGrantEffect.Granted)]);

        var response = await superAdmin.Client.GetAsync($"/api/v1/permissions/grants/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var grants = await response.Content.ReadFromJsonAsync<PermissionGrantsResponse>();
        Assert.NotNull(grants);

        var parent = grants.Grants.Single(x => x.Name == PermissionConstant.Users.Default);
        Assert.Equal(nameof(PermissionGrantEffect.Granted), parent.Direct);
        Assert.True(parent.Effective);
    }

    [Fact]
    public async Task User_grants_expose_the_inherited_effect_separately_from_the_direct_one()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var role = await CreateRoleAsync(superAdmin.Client);
        await ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            role.Id,
            [
                new PermissionGrant(PermissionConstant.Users.Default, PermissionGrantEffect.Granted),
                new PermissionGrant(PermissionConstant.Roles.Default, PermissionGrantEffect.Granted)
            ]);

        var user = await CreateUserAsync(superAdmin.Client, [role.Id]);
        await ReplaceGrantsAsync(
            PermissionGrantProviderNames.User,
            user.Id,
            [new PermissionGrant(PermissionConstant.Roles.Default, PermissionGrantEffect.Prohibited)]);

        var grants = await superAdmin.Client.GetFromJsonAsync<PermissionGrantsResponse>(
            $"/api/v1/permissions/grants/users/{user.Id}");
        Assert.NotNull(grants);

        // 仅来自角色：direct 为空，inherited 有值，最终生效。
        var inheritedOnly = grants.Grants.Single(x => x.Name == PermissionConstant.Users.Default);
        Assert.Null(inheritedOnly.Direct);
        Assert.Equal(nameof(PermissionGrantEffect.Granted), inheritedOnly.Inherited);
        Assert.True(inheritedOnly.Effective);

        // 角色允许 + 用户拒绝：两侧都要如实回传，且拒绝优先。
        var overridden = grants.Grants.Single(x => x.Name == PermissionConstant.Roles.Default);
        Assert.Equal(nameof(PermissionGrantEffect.Prohibited), overridden.Direct);
        Assert.Equal(nameof(PermissionGrantEffect.Granted), overridden.Inherited);
        Assert.False(overridden.Effective);
    }

#if (IncludeOpenIddict)
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
    public async Task User_permission_exceptions_require_a_dedicated_permission()
    {
        using var superAdmin = await factory.LoginAsync("admin", "Admin@123456");

        var target = await CreateUserAsync(superAdmin.Client);
        var operatorUser = await CreateUserAsync(superAdmin.Client);
        using var session = await factory.LoginAsync(operatorUser.Username, TestPassword);

        await GrantAsync(
            PermissionGrantProviderNames.User,
            operatorUser.Id,
            PermissionConstant.Users.Default,
            PermissionConstant.Users.Update);

        // 能看、能改资料，仍不能改权限：例外配置是独立的提权路径。
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/v1/permissions/grants/users/{target.Id}")).StatusCode);

        await GrantAsync(
            PermissionGrantProviderNames.User,
            operatorUser.Id,
            PermissionConstant.Users.ManagePermissions);
        Assert.Equal(
            HttpStatusCode.OK,
            (await session.Client.GetAsync($"/api/v1/permissions/grants/users/{target.Id}")).StatusCode);
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
                grants = new[] { new { name = PermissionConstant.Users.Default, effect = "Granted" } }
            });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 第二个管理员仍持有旧版本，保存必须被拒绝而不是覆盖前一次修改。
        var stale = await superAdmin.Client.PutAsJsonAsync(
            $"/api/v1/permissions/grants/roles/{role.Id}",
            new
            {
                expectedRevision = 0,
                grants = new[] { new { name = PermissionConstant.Roles.Default, effect = "Granted" } }
            });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await superAdmin.Client
            .GetFromJsonAsync<PermissionGrantsResponse>($"/api/v1/permissions/grants/roles/{role.Id}");
        Assert.NotNull(current);
        Assert.True(current.Grants.Single(x => x.Name == PermissionConstant.Users.Default).Effective);
        Assert.False(current.Grants.Single(x => x.Name == PermissionConstant.Roles.Default).Effective);
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
                grants = new[] { new { name = "App.NotDefined", effect = "Granted" } }
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
                DisplayName = "Audited user updated",
                IsActive = true
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
        IReadOnlyCollection<PermissionGrant> grants)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
        await manager.ReplaceGrantsAsync(providerName, providerKey.ToString(), grants);
    }

    private async Task GrantAllAsync(Guid roleId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();

        await manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            roleId.ToString(),
            [.. definitions.GetAll().Select(x => new PermissionGrant(x.Name, PermissionGrantEffect.Granted))]);
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

    private sealed record CurrentPermissionsResponse(string[] Permissions, bool IsSuperAdmin, string Revision);

    private sealed record PermissionGrantsResponse(long Revision, PermissionGrantStateResponse[] Grants);

    private sealed record PermissionGrantStateResponse(string Name, string? Direct, string? Inherited, bool Effective);
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
