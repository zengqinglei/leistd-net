#if (OpenIddictServer)
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
#if (IncludeMultiTenancy)
using System.Text.Json;
#endif
using CompanyName.ProjectName.Application.Auth.Options;
using CompanyName.ProjectName.Application.Users.Constants;
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
#if (IncludeMultiTenancy)
using Leistd.MultiTenancy.Context;
#endif
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class InternalAdministratorTests(ProjectWebApplicationFactory factory)
    : AuthorizationTestBase(factory), IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task The_endpoint_accepts_only_a_machine_token_with_the_administrator_scope()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var anonymous = CreateHttpsClient(Factory);
        var resourceScope = new OAuthResourceOptions().Resource;
        using var wrongScope = await CreateMachineClientAsync(Factory, admin.Client, resourceScope);
        using var machine = await CreateMachineClientAsync(Factory, admin.Client, AdministratorScopes.Read);
        var (clientId, secret) = await CreateClientAsync(admin.Client, resourceScope);
        var token = await AuthorizeAndExchangeAsync(Factory, admin, clientId, secret, resourceScope);
        using var user = CreateHttpsClient(Factory);
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var path = Path("host", Guid.CreateVersion7());

        using var unauthenticated = await anonymous.GetAsync(path);
        using var unscoped = await wrongScope.GetAsync(path);
        using var human = await user.GetAsync(path);
        using var permitted = await machine.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unscoped.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, human.StatusCode);
        Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
    }

    [Fact]
    public async Task Host_lookup_recognizes_only_available_super_admins_and_built_in_admin_members()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var machine = await CreateMachineClientAsync(Factory, admin.Client, AdministratorScopes.Read);

        var super = await AddUserAsync(null, user => user.MarkAsSuperAdmin());
        var member = await AddUserAsync(null, assignAdmin: true);
        var ordinary = await AddUserAsync(null);
        var disabled = await AddUserAsync(null, user => user.Disable(), assignAdmin: true);
        var deleted = await AddUserAsync(null, assignAdmin: true, deleted: true);

        Assert.True(await IsAdministratorAsync(machine, "host", super));
        Assert.True(await IsAdministratorAsync(machine, "host", member));
        Assert.False(await IsAdministratorAsync(machine, "host", ordinary));
        Assert.False(await IsAdministratorAsync(machine, "host", disabled));
        Assert.False(await IsAdministratorAsync(machine, "host", deleted));
        Assert.False(await IsAdministratorAsync(machine, "host", Guid.CreateVersion7()));
    }
#if (IncludeMultiTenancy)

    [Fact]
    public async Task Tenant_lookup_uses_tenant_membership_and_returns_not_found_for_unavailable_tenants()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var machine = await CreateMachineClientAsync(Factory, admin.Client, AdministratorScopes.Read);

        using var create = await admin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = "administrator-" + Guid.CreateVersion7().ToString("N")[..8],
            AdminEmail = "admin@example.test",
            AdminPassword = TestPassword
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var tenantId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var tenant = tenantId.ToString();
        var member = await AddUserAsync(tenantId, assignAdmin: true);
        var ordinary = await AddUserAsync(tenantId);
        var hostAdmin = await AddUserAsync(null, assignAdmin: true);

        Assert.True(await IsAdministratorAsync(machine, tenant, member));
        Assert.False(await IsAdministratorAsync(machine, tenant, ordinary));
        Assert.False(await IsAdministratorAsync(machine, tenant, hostAdmin));
        Assert.False(await IsAdministratorAsync(machine, "host", member));

        using var missing = await machine.GetAsync(Path(Guid.CreateVersion7().ToString(), member));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var deactivate = await admin.Client.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/activation", new { IsActive = false });
        deactivate.EnsureSuccessStatusCode();
        using var unavailable = await machine.GetAsync(Path(tenant, member));
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }
#endif

    private async Task<Guid> AddUserAsync(Guid? tenantId, Action<User>? change = null, bool assignAdmin = false, bool deleted = false)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
#if (IncludeMultiTenancy)
        using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId);
#endif
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var key = Guid.CreateVersion7().ToString("N");
        var user = new User("user_" + key, key + "@example.test");
        change?.Invoke(user);
        if (assignAdmin)
        {
            var role = await db.Roles.SingleAsync(role => role.Name == AdminConstant.RoleName && role.IsStatic);
            user.AssignRoles([role.Id]);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        if (deleted)
        {
            // 保留关联的 Admin 角色，用删除过滤器决定该用户是否可见。
            await db.Users.Where(row => row.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        }
        return user.Id;
    }

    private static string Path(string tenant, Guid user) => $"/api/v1/internal/administrators/{tenant}/{user}";

    private static async Task<bool> IsAdministratorAsync(HttpClient machine, string tenant, Guid user)
        => (await machine.GetFromJsonAsync<AdministratorStatusOutputDto>(Path(tenant, user)))!.IsAdministrator;
}
#endif
