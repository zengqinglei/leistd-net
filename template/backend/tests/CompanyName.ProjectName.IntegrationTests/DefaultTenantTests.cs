#if (TenantWorkspace && LocalIdentity)
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class DefaultTenantTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
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
}
#endif
