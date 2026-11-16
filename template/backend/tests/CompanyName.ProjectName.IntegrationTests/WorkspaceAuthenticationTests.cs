#if (TenantWorkspace && LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
#if (IncludeOperationRecords)
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
#endif

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>工作区入口矩阵：通过真实 HTTP 与默认策略、组件自然人策略验证主体边界。</summary>
public sealed class WorkspaceAuthenticationTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/v1/users", false, false, false, false, HttpStatusCode.Forbidden)]
    [InlineData("/api/v1/users", true, false, false, false, HttpStatusCode.OK)]
    [InlineData("/api/v1/permissions/current", true, false, false, false, HttpStatusCode.OK)]
    [InlineData("/api/v1/users", true, true, false, false, HttpStatusCode.Forbidden)]
    [InlineData("/api/v1/auth/me", false, false, false, false, HttpStatusCode.OK)]
    [InlineData("/api/v1/users", true, false, false, true, HttpStatusCode.OK)]
    [InlineData("/api/v1/users", true, false, true, false, HttpStatusCode.Forbidden)]
#if (IncludeNotifications)
    [InlineData("/api/v1/notifications/unread-count", true, false, false, false, HttpStatusCode.OK)]
#endif
#if (IncludeNotifications || IncludeRealTime)
    [InlineData(ProjectWebApplicationFactory.HubPath + "/negotiate?negotiateVersion=1&workspace={workspace}", true, false, false, false, HttpStatusCode.OK)]
    [InlineData(ProjectWebApplicationFactory.HubPath + "/negotiate?negotiateVersion=1&workspace={workspace}", false, false, false, false, HttpStatusCode.Forbidden)]
#endif
    public async Task Workspace_headers_use_linked_members_without_replacing_the_home_session(
        string path, bool granted, bool disabled, bool tenantDisabled, bool dedicated, HttpStatusCode expected)
    {
        using var session = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = "matrix-" + Guid.NewGuid().ToString("N")[..8];
        var connection = dedicated ? PostgreSqlTestDatabase.CreateDatabase() : null;
        try
        {
        var created = await session.Client.PostAsJsonAsync("/api/v1/tenants", new
        { Name = name, AdminEmail = "admin@example.com", AdminPassword = "Tenant@123456",
            Connections = connection is null ? Array.Empty<object>() : [new { Name = "default", ConnectionString = connection }] });
        created.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = document.RootElement.GetProperty("id").GetGuid();
        Guid homeId;
        using (var scope = factory.Services.CreateScope())
            homeId = (await scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>()
                .GetOneAsync(u => u.Username == "admin"))!.Id;
        if (granted)
        {
            var roles = (await session.Client.GetFromJsonAsync<List<RoleBriefOutputDto>>($"/api/v1/tenants/{tenantId}/staff/roles"))!;
            (await session.Client.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/staff",
                new GrantTenantStaffInputDto { HomeUserId = homeId, RoleIds = [roles.Single(r => r.Name == "Admin").Id] })).EnsureSuccessStatusCode();
        }
        if (disabled)
        {
            using var scope = factory.Services.CreateScope();
            using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId, name);
            using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
            var member = (await repository.GetOneAsync(u => u.HomeUserId == homeId))!;
            member.Disable();
            await repository.UpdateAsync(member);
            await uow.CompleteAsync();
        }
        if (tenantDisabled)
            (await session.Client.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/activation", new { IsActive = false })).EnsureSuccessStatusCode();
        var hub = path.Contains("/negotiate", StringComparison.Ordinal);
        using var request = new HttpRequestMessage(hub ? HttpMethod.Post : HttpMethod.Get, path.Replace("{workspace}", name, StringComparison.Ordinal));
        if (!hub) request.Headers.Add("X-Workspace", name);
        var response = await session.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Tenant-Invalid"));
        if (expected == HttpStatusCode.Forbidden)
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Workspace:Unavailable", error.RootElement.GetProperty("code").GetString());
        }
        (await session.Client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();
        (await session.Client.GetAsync("/api/v1/users")).EnsureSuccessStatusCode();
        if (expected == HttpStatusCode.OK && granted && !disabled && path == "/api/v1/users")
        {
            Assert.Contains("platform.admin", await response.Content.ReadAsStringAsync());
            using var removedEndpoint = await session.Client.PostAsync($"/api/v1/tenants/{tenantId}/impersonate", null);
            Assert.Equal(HttpStatusCode.NotFound, removedEndpoint.StatusCode);
#if (IncludeOperationRecords)
            Guid targetId;
            Guid memberId;
            string? actorName;
            using (var scope = factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId, name))
            using (scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
            {
                var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
                targetId = (await users.GetOneAsync(user => user.Username == "admin"))!.Id;
                var member = (await users.GetOneAsync(user => user.HomeUserId == homeId))!;
                memberId = member.Id;
                actorName = member.DisplayName ?? member.Username;
            }
            using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{targetId}/roles")
            { Content = JsonContent.Create(new { RoleIds = Array.Empty<Guid>() }) };
            update.Headers.Add("X-Workspace", name);
            (await session.Client.SendAsync(update)).EnsureSuccessStatusCode();
            using (var scope = factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId, name))
            using (scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
            {
                var db = await scope.ServiceProvider.GetRequiredService<IDbContextProvider<MyProjectDbContext>>().GetDbContextAsync();
                var record = await db.OperationRecords.SingleAsync(record => record.TargetId == targetId.ToString() && record.ActorId == memberId.ToString());
                Assert.Equal(tenantId, record.TenantId);
                Assert.Equal(tenantId, record.ActorTenantId);
                Assert.Equal(actorName, record.ActorName);
            }
#endif
        }
        }
        finally { if (connection is not null) PostgreSqlTestDatabase.DropDatabase(connection); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tenant_users_can_only_select_their_own_workspace(bool foreign)
    {
        using var host = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = "own-" + Guid.NewGuid().ToString("N")[..8];
        using var created = await host.Client.PostAsJsonAsync("/api/v1/tenants", new
        { Name = name, AdminEmail = "admin@example.com", AdminPassword = "Tenant@123456" });
        created.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = body.RootElement.GetProperty("id").GetGuid();
        using var tenantClient = ProjectWebApplicationFactory.CreateProjectClient(factory);
        tenantClient.DefaultRequestHeaders.Add(MultiTenancyOptions.DefaultHeaderName, tenantId.ToString());
        using var login = await tenantClient.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = "admin", Password = "Tenant@123456" });
        login.EnsureSuccessStatusCode();
        tenantClient.DefaultRequestHeaders.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0])));
        tenantClient.DefaultRequestHeaders.Add("X-Workspace", foreign ? "another-tenant" : name);
        using var response = await tenantClient.GetAsync("/api/v1/users");
        Assert.Equal(foreign ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response.StatusCode);
        if (foreign)
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Workspace:Forbidden", error.RootElement.GetProperty("code").GetString());
        }
        using var workspaces = await tenantClient.GetAsync("/api/v1/auth/me/workspaces");
        workspaces.EnsureSuccessStatusCode();
        Assert.Equal(name, Assert.Single((await workspaces.Content.ReadFromJsonAsync<List<WorkspaceOutputDto>>())!).Name);
    }

}
#endif
