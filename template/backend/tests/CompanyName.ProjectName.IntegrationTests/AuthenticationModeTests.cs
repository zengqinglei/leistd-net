using Leistd.Authorization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections;
using Leistd.Authorization;
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
        await using var scope = factory.Services.CreateAsyncScope();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        Assert.NotNull(await scopeManager.FindByNameAsync("tenant-routing.read"));
        Assert.NotNull(await scopeManager.FindByNameAsync("tenant-migration.read"));
    }

    /// <summary>
    /// 内部控制面策略的完整判定矩阵
    /// </summary>
    /// <remarks>
    /// <para>这两条策略保护的端点直接返回租户连接配置（数据落在哪个库、Secret 引用），
    /// 且接受<b>任意</b> <c>tenantId</c>——控制库是普通 <c>DbContext</c>、没有租户过滤器。
    /// 因此它们必须只对机器主体开放：任何自然人主体命中都是跨租户越权。</para>
    /// <para>因此判定里<b>不能</b>有"或者是超管"这一支：超管 claim 不经权限检查器、不受侧别边界
    /// 约束，只要有一个租户内的主体拿到它，就能读到别的租户的 Secret 引用。这组用例把每一条路
    /// 都钉死，包括那条看起来很自然的旁路。</para>
    /// <para>认证方案层面的收窄（只列 Bearer、Cookie 到不了）无法在此断言——
    /// <c>IAuthorizationService.AuthorizeAsync</c> 不经过认证中间件；它由策略注册本身保证。</para>
    /// </remarks>
    [Theory]
    // 机器主体 + 正确 scope：唯一放行的组合
    [InlineData("client:svc", "tenant-routing.read", true)]
    // 机器主体 + 错误 scope
    [InlineData("client:svc", "tenant-migration.read", false)]
    // 机器主体 + 无 scope
    [InlineData("client:svc", null, false)]
    // 自然人主体（GUID sub）+ 正确 scope：用户令牌不得命中机器端点
    [InlineData("8f14e45f-ea6a-4c4b-9b2b-7c1f0a2d3e4f", "tenant-routing.read", false)]
    // 完全没有 sub
    [InlineData(null, "tenant-routing.read", false)]
    public async Task Runtime_policy_only_admits_machine_principals(
        string? subject,
        string? scope,
        bool expected)
    {
        var result = await AuthorizeAsync(TenantConnectionPolicies.RuntimeRead, subject, scope);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(TenantConnectionPolicies.RuntimeRead)]
    [InlineData(TenantConnectionPolicies.MigrationRead)]
    public async Task Super_admin_claim_does_not_admit_machine_endpoints(string policyName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(OpenIddictConstants.Claims.Subject, "8f14e45f-ea6a-4c4b-9b2b-7c1f0a2d3e4f"),
                new Claim(Leistd.Security.Claims.CustomClaimTypes.IsSuperAdmin, "true")
            ],
            "test"));

        var result = await authorization.AuthorizeAsync(principal, resource: null, policyName);

        Assert.False(result.Succeeded);
    }

    /// <summary>两条 scope 彼此独立：Runtime 的 scope 读不到 Migration</summary>
    [Fact]
    public async Task Runtime_routing_scope_cannot_read_migration_metadata()
    {
        Assert.True(await AuthorizeAsync(
            TenantConnectionPolicies.RuntimeRead, "client:svc", "tenant-routing.read"));
        Assert.False(await AuthorizeAsync(
            TenantConnectionPolicies.MigrationRead, "client:svc", "tenant-routing.read"));
    }

    private async Task<bool> AuthorizeAsync(string policyName, string? subject, string? scope)
    {
        var claims = new List<Claim>();
        if (subject is not null)
        {
            claims.Add(new Claim(OpenIddictConstants.Claims.Subject, subject));
        }

        if (scope is not null)
        {
            claims.Add(new Claim(OpenIddictConstants.Claims.Scope, scope));
        }

        await using var serviceScope = factory.Services.CreateAsyncScope();
        var authorization = serviceScope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var result = await authorization.AuthorizeAsync(principal, resource: null, policyName);
        return result.Succeeded;
    }
}
#endif
#if (!LocalIdentity)

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
