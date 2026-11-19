#if (TenantWorkspace && LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using CompanyName.ProjectName.Domain.Users.Errors;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Domain.Auth.Entities;
#if (ExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Abstractions;
#endif
using Leistd.ExceptionHandling;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.UnitOfWork;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class TenantStaffTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData("password")]
    [InlineData("email")]
    [InlineData("two-factor")]
    public void Linked_accounts_refuse_credentials_and_remain_usable_members(string operation)
    {
        var user = User.CreatePlatformAccount(Guid.NewGuid(), "operator", "Operator");
        var error = Assert.Throws<BusinessException>(() =>
        {
            switch (operation)
            {
                case "password": user.UpdatePasswordHash("hash"); break;
                case "email": user.UpdateProfile("operator", "test@example.com", "Operator", null); break;
                case "two-factor": user.EnableTwoFactor("secret", ["hash"], 1); break;
            }
        });
        Assert.Equal(UserErrorCodes.PlatformAccountImmutable, error.Code);
        Assert.Null(user.PasswordHash);
        Assert.Equal("", user.Email);
        Assert.Equal(UserAccessStatus.Allowed,
            user.GetAccessStatus(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task The_list_returns_the_stored_linked_username_rather_than_the_home_username()
    {
        using var session = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = "linked-" + Guid.NewGuid().ToString("N")[..8];
        using var created = await session.Client.PostAsJsonAsync("/api/v1/tenants", new
        { Name = name, AdminEmail = "admin@example.com", AdminPassword = "Tenant@123456" });
        created.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = body.RootElement.GetProperty("id").GetGuid();
        Guid homeUserId;
        using (var scope = factory.Services.CreateScope())
        {
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(null))
            using (scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
                homeUserId = (await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
                    .GetOneAsync(u => u.Username == "admin"))!.Id;
            // 关联账号保留早先的用户名（例如宿主改名前授予过），列表要返回库里的真实值
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId, name))
            using (var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
            {
                await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
                    .InsertAsync(User.CreatePlatformAccount(homeUserId, "renamed", "Renamed"));
                await unit.CompleteAsync();
            }
        }
        var roles = await session.Client.GetFromJsonAsync<List<RoleBriefOutputDto>>($"/api/v1/tenants/{tenantId}/staff/roles");
        var url = $"/api/v1/tenants/{tenantId}/staff";
        (await session.Client.PostAsJsonAsync(url, new GrantTenantStaffInputDto
        { HomeUserId = homeUserId, RoleIds = [roles!.Single(r => r.Name == "Admin").Id] })).EnsureSuccessStatusCode();

        var member = Assert.Single((await session.Client.GetFromJsonAsync<List<TenantStaffOutputDto>>(url))!);
        Assert.Equal("admin", member.Username);
        Assert.Equal("platform.renamed", member.LinkedUsername);
    }

    [Fact]
    public async Task Grant_remove_and_regrant_restore_the_same_member_and_roles()
    {
        using var session = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var created = await session.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "staff-" + Guid.NewGuid().ToString("N")[..8], DisplayName = "Staff test",
            AdminEmail = "admin@example.com", AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = body.RootElement.GetProperty("id").GetGuid();
        Guid homeUserId;
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(null))
        using (scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
            homeUserId = (await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
                .GetOneAsync(u => u.Username == "admin"))!.Id;
        var roles = await session.Client.GetFromJsonAsync<List<RoleBriefOutputDto>>($"/api/v1/tenants/{tenantId}/staff/roles");
        var input = new GrantTenantStaffInputDto { HomeUserId = homeUserId, RoleIds = [roles!.Single(r => r.Name == "Admin").Id] };
        var url = $"/api/v1/tenants/{tenantId}/staff";
        (await session.Client.PostAsJsonAsync(url, input)).EnsureSuccessStatusCode();
        var member = Assert.Single((await session.Client.GetFromJsonAsync<List<TenantStaffOutputDto>>(url))!);
        Assert.Equal(homeUserId, member.HomeUserId);
        Assert.Equal("platform.admin", member.LinkedUsername);
        Assert.Equal("Admin", Assert.Single(member.Roles).Name);
        (await session.Client.DeleteAsync($"{url}/{homeUserId}")).EnsureSuccessStatusCode();
        Assert.Empty((await session.Client.GetFromJsonAsync<List<TenantStaffOutputDto>>(url))!);
        (await session.Client.PostAsJsonAsync(url, input)).EnsureSuccessStatusCode();
        Assert.Equal(member.MemberId, Assert.Single((await session.Client.GetFromJsonAsync<List<TenantStaffOutputDto>>(url))!).MemberId);
    }
    [Theory]
    [InlineData("password")]
    [InlineData("profile")]
    [InlineData("two-factor")]
    [InlineData("delete")]
    public async Task Tenant_administrators_cannot_add_credentials_or_delete_linked_accounts(string operation)
    {
        using var host = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = "credentials-" + Guid.NewGuid().ToString("N")[..8];
        using var created = await host.Client.PostAsJsonAsync("/api/v1/tenants", new
        { Name = name, AdminEmail = "admin@example.com", AdminPassword = "Tenant@123456" });
        created.EnsureSuccessStatusCode();
        using var tenantJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = tenantJson.RootElement.GetProperty("id").GetGuid();
        using var homeJson = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/auth/me"));
        var homeId = homeJson.RootElement.GetProperty("id").GetGuid();
        var roles = (await host.Client.GetFromJsonAsync<List<RoleBriefOutputDto>>($"/api/v1/tenants/{tenantId}/staff/roles"))!;
        var url = $"/api/v1/tenants/{tenantId}/staff";
        (await host.Client.PostAsJsonAsync(url, new GrantTenantStaffInputDto
        { HomeUserId = homeId, RoleIds = [roles.Single(role => role.Name == "Admin").Id] })).EnsureSuccessStatusCode();
        var member = Assert.Single((await host.Client.GetFromJsonAsync<List<TenantStaffOutputDto>>(url))!);
        using var tenant = ProjectWebApplicationFactory.CreateProjectClient(factory);
        tenant.DefaultRequestHeaders.Add(MultiTenancyOptions.DefaultHeaderName, tenantId.ToString());
        using var login = await tenant.PostAsJsonAsync("/api/v1/auth/session-login", new
        { UsernameOrEmail = "admin", Password = "Tenant@123456" });
        login.EnsureSuccessStatusCode();
        tenant.DefaultRequestHeaders.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0])));
        using var ordinaryResponse = await tenant.PostAsJsonAsync("/api/v1/users", new
        { Username = "ordinary", Email = "ordinary@example.test", Password = "Ordinary!Passw0rd" });
        ordinaryResponse.EnsureSuccessStatusCode();
        using var ordinaryJson = JsonDocument.Parse(await ordinaryResponse.Content.ReadAsStringAsync());
        var ordinaryId = ordinaryJson.RootElement.GetProperty("id").GetGuid();
        Task<HttpResponseMessage> Change(Guid id) => operation switch
        {
            "password" => tenant.PostAsJsonAsync($"/api/v1/users/{id}/reset-password", new { Password = "Updated!Passw0rd" }),
            "profile" => tenant.PutAsJsonAsync($"/api/v1/users/{id}", new { Email = "changed@example.test", DisplayName = "Changed" }),
            "two-factor" => tenant.PostAsJsonAsync($"/api/v1/users/{id}/reset-two-factor", new { }),
            "delete" => tenant.DeleteAsync($"/api/v1/users/{id}"),
            _ => throw new InvalidOperationException(operation)
        };
        using var denied = await Change(member.MemberId);
        Assert.False(denied.IsSuccessStatusCode);
        using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.Equal(UserErrorCodes.PlatformAccountImmutable, problem.RootElement.GetProperty("code").GetString());
        using var accepted = await Change(ordinaryId);
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        using var anonymous = ProjectWebApplicationFactory.CreateProjectClient(factory);
        anonymous.DefaultRequestHeaders.Add(MultiTenancyOptions.DefaultHeaderName, tenantId.ToString());
        using var linkedLogin = await anonymous.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = "platform.admin", Password = "Updated!Passw0rd" });
        using var missingLogin = await anonymous.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = "platform.missing", Password = "Updated!Passw0rd" });
        Assert.Equal(missingLogin.StatusCode, linkedLogin.StatusCode);
        Assert.False(linkedLogin.IsSuccessStatusCode);
        Assert.False(linkedLogin.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("passkey")]
#if (ExternalLogin)
    [InlineData("external")]
#endif
    public async Task Credential_domain_services_reject_linked_members_before_persisting_connections(string kind)
    {
        using var host = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = "binding-" + Guid.NewGuid().ToString("N")[..8];
        using var created = await host.Client.PostAsJsonAsync("/api/v1/tenants", new
        { Name = name, AdminEmail = "admin@example.com", AdminPassword = "Tenant@123456" });
        created.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = body.RootElement.GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        using var tenant = services.GetRequiredService<ICurrentTenant>().Change(tenantId, name);
        using var unit = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var repository = services.GetRequiredService<IRepository<User, Guid>>();
        var linked = User.CreatePlatformAccount(Guid.NewGuid(), "staff", "Staff");
        var ordinary = new User("ordinary", "ordinary@example.test");
        await repository.InsertAsync(linked);
        await repository.InsertAsync(ordinary);
        async Task Bind(User user)
        {
            if (kind == "passkey")
                await services.GetRequiredService<PasskeyDomainService>().RegisterAsync(user,
                    new PasskeyCredentialData(Guid.NewGuid().ToByteArray(), [1, 2, 3], user.Id.ToByteArray(), 0, Guid.Empty, [], false, false), "Test", ["recovery-hash"]);
#if (ExternalLogin)
            else
                await services.GetRequiredService<ExternalAuthDomainService>().LinkAsync(user, "github", new ExternalUserInfo
                { ProviderId = Guid.NewGuid().ToString(), ProviderAccountLabel = "test", Email = "provider@example.test", EmailVerified = true });
#endif
        }
        var denied = await Assert.ThrowsAsync<BusinessException>(() => Bind(linked));
        Assert.Equal(UserErrorCodes.PlatformAccountImmutable, denied.Code);
        await Bind(ordinary);
        await unit.CompleteAsync();
        using var read = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        Assert.False(await services.GetRequiredService<IRepository<UserPasskey, Guid>>().AnyAsync(passkey => passkey.UserId == linked.Id));
#if (ExternalLogin)
        Assert.False(await services.GetRequiredService<IRepository<ExternalLoginConnection, Guid>>().AnyAsync(connection => connection.UserId == linked.Id));
#endif
    }

    [Fact]
    public async Task Staff_permission_alone_can_read_candidates_and_grant_access_without_user_administration()
    {
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var tenantResponse = await admin.Client.PostAsJsonAsync("/api/v1/tenants", new
        { Name = "candidate-" + suffix, AdminEmail = "admin@example.test", AdminPassword = "Tenant@123456" });
        tenantResponse.EnsureSuccessStatusCode();
        using var tenantBody = JsonDocument.Parse(await tenantResponse.Content.ReadAsStringAsync());
        var tenantId = tenantBody.RootElement.GetProperty("id").GetGuid();
        using var roleResponse = await admin.Client.PostAsJsonAsync("/api/v1/roles", new { Name = "staff_" + suffix, DisplayName = "Staff access" });
        roleResponse.EnsureSuccessStatusCode();
        using var roleBody = JsonDocument.Parse(await roleResponse.Content.ReadAsStringAsync());
        var roleId = roleBody.RootElement.GetProperty("id").GetGuid();
        var grantsUrl = $"/api/v1/permissions/grants/roles/{roleId}";
        using var grants = JsonDocument.Parse(await admin.Client.GetStringAsync(grantsUrl));
        (await admin.Client.PutAsJsonAsync(grantsUrl, new { ExpectedVersion = grants.RootElement.GetProperty("version").GetInt64(), PermissionNames = new[] { "App.Tenants.StaffAccess" } })).EnsureSuccessStatusCode();
        var username = "staff_" + suffix;
        (await admin.Client.PostAsJsonAsync("/api/v1/users", new
        { Username = username, Email = username + "@example.test", Password = "StaffPermission!Passw0rd", RoleIds = new[] { roleId } })).EnsureSuccessStatusCode();
        using var staff = await factory.LoginAsync(username, "StaffPermission!Passw0rd");
        using var me = JsonDocument.Parse(await staff.Client.GetStringAsync("/api/v1/auth/me"));
        var homeId = me.RootElement.GetProperty("id").GetGuid();
        using var users = await staff.Client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        var candidates = (await staff.Client.GetFromJsonAsync<List<TenantStaffCandidateOutputDto>>($"/api/v1/tenants/{tenantId}/staff/candidates?keyword={username}"))!;
        Assert.Equal(homeId, Assert.Single(candidates).Id);
        var roles = (await staff.Client.GetFromJsonAsync<List<RoleBriefOutputDto>>($"/api/v1/tenants/{tenantId}/staff/roles"))!;
        (await staff.Client.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/staff", new GrantTenantStaffInputDto
        { HomeUserId = homeId, RoleIds = [roles.Single(role => role.Name == "Admin").Id] })).EnsureSuccessStatusCode();
    }

}
#endif
