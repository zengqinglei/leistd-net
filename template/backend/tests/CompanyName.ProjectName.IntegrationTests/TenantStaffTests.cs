#if (TenantWorkspace && LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Errors;
using Leistd.ExceptionHandling;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Context;
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
        Assert.Equal(CompanyName.ProjectName.Domain.Users.ValueObjects.UserAccessStatus.Allowed,
            user.GetAccessStatus(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)));
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
        Assert.Equal("Admin", Assert.Single(member.Roles).Name);
        (await session.Client.DeleteAsync($"{url}/{homeUserId}")).EnsureSuccessStatusCode();
        Assert.Empty((await session.Client.GetFromJsonAsync<List<TenantStaffOutputDto>>(url))!);
        (await session.Client.PostAsJsonAsync(url, input)).EnsureSuccessStatusCode();
        Assert.Equal(member.MemberId, Assert.Single((await session.Client.GetFromJsonAsync<List<TenantStaffOutputDto>>(url))!).MemberId);
    }
}
#endif
