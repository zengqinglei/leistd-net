using Microsoft.Extensions.DependencyInjection;
#if (OpenIddictServer || RemoteTokenAuth)
using Microsoft.Extensions.Options;
#endif
#if (RemoteTokenAuth)
using Leistd.Authorization.Definitions;
using CompanyName.ProjectName.Api;
using CompanyName.ProjectName.Api.Auth.Authorization;
using Leistd.Security.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
#endif
#if (OpenIddictServer)
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.Auth.Options;
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using OpenIddict.Abstractions;
using OpenIddict.Server;

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
    public async Task Initialization_removes_a_stored_scope_outside_the_catalog()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var staleName = $"retired-{Guid.NewGuid():N}";
        await scopeManager.CreateAsync(new OpenIddictScopeDescriptor { Name = staleName });
        Assert.NotNull(await scopeManager.FindByNameAsync(staleName));

        await scope.ServiceProvider.GetRequiredService<ISystemInitializer>().InitializeAsync();

        Assert.Null(await scopeManager.FindByNameAsync(staleName));
    }

    [Fact]
    public async Task Identity_registers_its_service_scope_and_omits_retired_machine_scopes()
    {
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        await using var scope = factory.Services.CreateAsyncScope();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var resource = scope.ServiceProvider.GetRequiredService<IOptions<OAuthResourceOptions>>().Value.Resource;

        var available = await admin.Client.GetFromJsonAsync<OpenApplicationScopeOutputDto[]>("/api/v1/open-applications/scopes");
        Assert.NotNull(available);
        Assert.Contains(available, option => option.Name == resource && option.Audience == resource && !option.MachineOnly);
        Assert.NotNull(await scopeManager.FindByNameAsync(resource));

        string[] retiredScopes =
        [
            "tenant-routing.read", "tenant-migration.read", "account-preferences.read",
            "system-directory.read", "administrators.read"
        ];
        foreach (var retired in retiredScopes)
        {
            Assert.DoesNotContain(available, option => option.Name == retired);
            Assert.Null(await scopeManager.FindByNameAsync(retired));
        }
    }
}
#endif
#if (RemoteTokenAuth)

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class AuthenticationModeTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData("8f14e45f-ea6a-4c4b-9b2b-7c1f0a2d3e4f", true)]
    [InlineData("client:orders", false)]
    [InlineData("orders", false)]
    [InlineData(null, false)]
    public async Task Resource_default_and_current_user_policies_require_a_natural_person(string? subject, bool expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSecurity();
        services.AddApiAuthorization();
        // 这里只看自然人断言：成员状态的判定要查库，由 ResourceMemberAccessTests 在真实宿主上验证，此处判为满足
        services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(LocalMemberAccessHandler)));
        services.AddSingleton<IAuthorizationHandler, SatisfiedLocalMember>();
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var claims = subject is null ? Array.Empty<Claim>() : [new Claim("sub", subject)];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "verified-token"));

        Assert.Equal(expected, (await authorization.AuthorizeAsync(user, null, options.DefaultPolicy)).Succeeded);
        Assert.Equal(expected, (await authorization.AuthorizeAsync(user, null, options.GetPolicy(ApiPolicies.CurrentUser)!)).Succeeded);
    }

    [Fact]
    public void Resource_default_and_current_user_policies_require_an_enabled_local_member()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSecurity();
        services.AddApiAuthorization();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        // Hub 握手与方法调用复评取默认策略，组件自用端点取 CurrentUser：两条都要带成员要求
        Assert.Contains(options.DefaultPolicy.Requirements, requirement => requirement is LocalMemberAccessRequirement);
        Assert.Contains(options.GetPolicy(ApiPolicies.CurrentUser)!.Requirements, requirement => requirement is LocalMemberAccessRequirement);
    }

    private sealed class SatisfiedLocalMember : AuthorizationHandler<LocalMemberAccessRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LocalMemberAccessRequirement requirement)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

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
