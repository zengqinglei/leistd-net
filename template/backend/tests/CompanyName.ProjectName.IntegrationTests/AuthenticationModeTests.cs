#if (IdentityService)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy.EntityFrameworkCore;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class AuthenticationModeTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public void Identity_access_tokens_expire_after_ten_minutes()
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>()
            .CurrentValue;

        Assert.Equal(TimeSpan.FromMinutes(10), options.AccessTokenLifetime);
    }

    [Fact]
    public async Task Identity_registers_runtime_and_migration_tenant_connection_scopes()
    {
        var scopeManager = factory.Services.GetRequiredService<IOpenIddictScopeManager>();

        Assert.NotNull(await scopeManager.FindByNameAsync("tenant-routing.read"));
        Assert.NotNull(await scopeManager.FindByNameAsync("tenant-migration.read"));
    }

    [Fact]
    public async Task Runtime_routing_scope_cannot_read_migration_metadata()
    {
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(OpenIddictConstants.Claims.Scope, "tenant-routing.read")],
            "test"));

        var runtime = await authorization.AuthorizeAsync(
            principal,
            resource: null,
            policyName: "TenantConnection.RuntimeRead");
        var migration = await authorization.AuthorizeAsync(
            principal,
            resource: null,
            policyName: "TenantConnection.MigrationRead");

        Assert.True(runtime.Succeeded);
        Assert.False(migration.Succeeded);
    }

    [Fact]
    public void Identity_control_and_tenant_business_models_are_separated()
    {
        using var scope = factory.Services.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<IdentityControlDbContext>();
        var business = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();

        Assert.NotNull(control.Model.FindEntityType(typeof(TenantRecord)));
        Assert.Null(control.Model.FindEntityType(typeof(User)));
        Assert.Null(business.Model.FindEntityType(typeof(TenantRecord)));
        Assert.NotNull(business.Model.FindEntityType(typeof(User)));
    }
}
#endif
#if (ResourceService)
using Leistd.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class AuthenticationModeTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public void Resource_does_not_publish_identity_tenant_control_permissions()
    {
        var definitions = factory.Services.GetRequiredService<IPermissionDefinitionManager>();

        Assert.DoesNotContain(
            definitions.GetAll(),
            definition => definition.Name.EndsWith(".Tenants", StringComparison.Ordinal));
    }
}
#endif
