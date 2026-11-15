#if (TenantWorkspace && LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>工作区入口矩阵：通过真实 HTTP 与默认策略、组件自然人策略验证主体边界。</summary>
public sealed class WorkspaceAuthenticationTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/v1/users", false, false, HttpStatusCode.Forbidden)]
    [InlineData("/api/v1/users", true, false, HttpStatusCode.OK)]
    [InlineData("/api/v1/permissions/current", true, false, HttpStatusCode.OK)]
    [InlineData("/api/v1/users", true, true, HttpStatusCode.Forbidden)]
    [InlineData("/api/v1/auth/me", false, false, HttpStatusCode.OK)]
    public async Task Workspace_headers_use_linked_members_without_replacing_the_home_session(
        string path, bool granted, bool disabled, HttpStatusCode expected)
    {
        using var session = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = "matrix-" + Guid.NewGuid().ToString("N")[..8];
        var created = await session.Client.PostAsJsonAsync("/api/v1/tenants", new
        { Name = name, AdminEmail = "admin@example.com", AdminPassword = "Tenant@123456" });
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
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Workspace", name);
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
        if (granted && !disabled && path == "/api/v1/users")
            Assert.Contains("platform.admin", await response.Content.ReadAsStringAsync());
    }
}
#endif
