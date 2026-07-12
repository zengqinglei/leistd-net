#if (IncludeIdentity)
using System.Net;
using System.Net.Http.Json;
#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.Provider;
#endif
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.Entities;
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

        var member = await CreateUserAsync(superAdmin.Client, roles: [MemberRoleName]);
        using var memberSession = await factory.LoginAsync(member.Username, TestPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetUsersAsync(memberSession.Client)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
            await grants.GrantToUserAsync(PermissionConstant.Users.Default, member.Id.ToString());
        }
        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(memberSession.Client)).StatusCode);

        var adminRoleUser = await CreateUserAsync(superAdmin.Client, roles: [AdminConstant.RoleName]);
        using var adminRoleSession = await factory.LoginAsync(adminRoleUser.Username, TestPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetUsersAsync(adminRoleSession.Client)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var adminRoleId = await db.Roles
                .Where(role => role.Name == AdminConstant.RoleName)
                .Select(role => role.Id)
                .SingleAsync();
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
            await grants.GrantToRoleAsync(PermissionConstant.Users.Default, adminRoleId.ToString());
        }
        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(adminRoleSession.Client)).StatusCode);
    }
#else
    [Fact]
    public async Task Authenticated_users_should_not_require_role_permissions_when_roles_are_disabled()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var member = await CreateUserAsync(admin.Client, roles: [MemberRoleName]);
        using var memberSession = await factory.LoginAsync(member.Username, TestPassword);

        Assert.Equal(HttpStatusCode.OK, (await GetUsersAsync(memberSession.Client)).StatusCode);
    }
#endif

    [Fact]
    public async Task Audit_fields_should_follow_the_authenticated_operator()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var created = await CreateUserAsync(admin.Client, roles: [MemberRoleName]);

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
                IsActive = true,
                Roles = [MemberRoleName]
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
    private const string MemberRoleName = "Member";

    private static Task<HttpResponseMessage> GetUsersAsync(HttpClient client)
        => client.GetAsync("/api/v1/users?offset=0&limit=10");

    private static async Task<UserManagementOutputDto> CreateUserAsync(HttpClient client, List<string> roles)
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
                Roles = roles
            });

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Create user failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<UserManagementOutputDto>())!;
    }
}
#endif
