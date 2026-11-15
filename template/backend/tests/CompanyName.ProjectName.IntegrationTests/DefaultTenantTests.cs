#if (LocalIdentity && IncludeMultiTenancy)
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.Initialization;
#if (TenantWorkspace)
using CompanyName.ProjectName.Application.Tenants.Dtos;
#else
using System.Text.Json;
using Leistd.MultiTenancy.AspNetCore.Options;
#endif
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class DefaultTenantTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
#if (TenantWorkspace)
    [Fact]
    public async Task First_start_creates_one_default_workspace_and_repeated_initialization_leaves_it_unchanged()
    {
        using var configured = factory.WithWebHostBuilder(builder => builder
            .UseSetting("MultiTenancy:DefaultTenant:Name", "default-acme")
            .UseSetting("MultiTenancy:DefaultTenant:DisplayName", "Default workspace"));
        using var session = await ProjectWebApplicationFactory.LoginAsync(configured, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var before = (await session.Client.GetFromJsonAsync<List<WorkspaceOutputDto>>("/api/v1/auth/me/workspaces"))!;
        var workspace = Assert.Single(before);
        Assert.Equal("default-acme", workspace.Name);
        using (var scope = configured.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISystemInitializer>().InitializeAsync();
        var after = (await session.Client.GetFromJsonAsync<List<WorkspaceOutputDto>>("/api/v1/auth/me/workspaces"))!;
        Assert.Equal(workspace.TenantId, Assert.Single(after).TenantId);
        using var changedConfiguration = configured.WithWebHostBuilder(builder => builder.UseSetting("MultiTenancy:DefaultTenant:Name", "must-not-create"));
        using var repeated = await ProjectWebApplicationFactory.LoginAsync(changedConfiguration, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var unchanged = (await repeated.Client.GetFromJsonAsync<List<WorkspaceOutputDto>>("/api/v1/auth/me/workspaces"))!;
        Assert.Equal(workspace.TenantId, Assert.Single(unchanged).TenantId);
    }
#else
    [Fact]
    public async Task Default_form_seeds_a_tenant_administrator_with_the_configured_default_admin_credentials()
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting("MultiTenancy:DefaultTenant:Name", "default-private"));
        using var host = await ProjectWebApplicationFactory.LoginAsync(configured, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var tenants = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/tenants"));
        var tenant = Assert.Single(tenants.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("default-private", tenant.GetProperty("name").GetString());
        var tenantId = tenant.GetProperty("id").GetGuid();
        using var client = ProjectWebApplicationFactory.CreateProjectClient(configured);
        client.DefaultRequestHeaders.Add(MultiTenancyOptions.DefaultHeaderName, tenantId.ToString());
        using var login = await client.PostAsJsonAsync("/api/v1/auth/session-login", new { UsernameOrEmail = "admin", Password = ProjectWebApplicationFactory.TestAdminPassword });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0])));
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/v1/auth/me"));
        Assert.Equal(tenantId, me.RootElement.GetProperty("tenantId").GetGuid());
        using (var scope = configured.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISystemInitializer>().InitializeAsync();
        using var after = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/tenants"));
        Assert.Equal(tenantId, Assert.Single(after.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }
#endif

}
#endif
