#if (LocalIdentity && IncludeMultiTenancy)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
#endif
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Repositories;
using CompanyName.ProjectName.Infrastructure.Persistence.Repositories;
using Leistd.Auditing.Abstractions;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy.Context;
#if (IncludeMultiTenancy)
using Leistd.MultiTenancy.Tenancy;
#endif
using Leistd.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 用户模块的自定义仓储：查询只看当前租户、未删除的行，按名称排序；登记后通用仓储接口也解析到同一实现。
/// </summary>
/// <remarks>
/// 过滤是否生效用"关掉过滤器后能看到"来证伪：被排除的行确实还在库里，排除它们的是全局过滤器，
/// 而不是删除把行物理删掉了或种子没写进去。
/// </remarks>
public sealed class UserModuleRepositoryTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task Custom_repositories_also_serve_the_default_repository_interfaces()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        Assert.IsType<EfCoreUserRepository>(services.GetRequiredService<IUserRepository>());
        Assert.IsType<EfCoreUserRepository>(services.GetRequiredService<IRepository<User, Guid>>());
        Assert.IsType<EfCoreRoleRepository>(services.GetRequiredService<IRoleRepository>());
        Assert.IsType<EfCoreRoleRepository>(services.GetRequiredService<IRepository<Role, Guid>>());
    }

    [Fact]
    public async Task Role_names_are_sorted_and_exclude_deleted_roles_and_deleted_assignments()
    {
        var suffix = NewSuffix();
        var (userId, names) = await SeedUserWithRolesAsync(tenantId: null, suffix);

        await using var scope = factory.Services.CreateAsyncScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        Assert.Equal([names.First, names.Second], await userRepository.GetRoleNamesAsync(userId));

        using (scope.ServiceProvider.GetRequiredService<IDataFilter>().Disable<ISoftDelete>())
        {
            Assert.Equal(
                [names.First, names.Second, names.DeletedRole, names.DeletedAssignment],
                await userRepository.GetRoleNamesAsync(userId));
        }
    }

    [Fact]
    public async Task Role_names_of_a_user_without_roles_are_empty()
    {
        var suffix = NewSuffix();
        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>().InsertAsync(NewUser(suffix));
            await unitOfWork.CompleteAsync();
            userId = user.Id;
        }

        await using var query = factory.Services.CreateAsyncScope();
        Assert.Empty(await query.ServiceProvider.GetRequiredService<IUserRepository>().GetRoleNamesAsync(userId));
    }

    [Fact]
    public async Task Default_roles_are_sorted_and_exclude_deleted_and_non_default_roles()
    {
        var suffix = NewSuffix();
        var names = await SeedRolesAsync(tenantId: null, suffix);

        await using var scope = factory.Services.CreateAsyncScope();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

        var defaults = await roleRepository.GetDefaultRolesAsync();
        // 宿主里还有种子写入的默认角色：只核对本用例写入的行及其相对顺序
        Assert.All(defaults, role => Assert.True(role.IsDefault));
        Assert.Equal(
            [names.First, names.Second],
            defaults.Select(role => role.Name).Where(name => name.EndsWith(suffix, StringComparison.Ordinal)));

        using (scope.ServiceProvider.GetRequiredService<IDataFilter>().Disable<ISoftDelete>())
        {
            var withDeleted = (await roleRepository.GetDefaultRolesAsync()).Select(role => role.Name);
            Assert.Equal(
                [names.First, names.Second, names.DeletedRole],
                withDeleted.Where(name => name.EndsWith(suffix, StringComparison.Ordinal)));
        }
    }

#if (IncludeMultiTenancy)
    [Fact]
    public async Task Role_names_only_come_from_the_current_tenant()
    {
        var suffix = NewSuffix();
        var tenantId = await NewTenantAsync();
        var (hostUserId, hostNames) = await SeedUserWithRolesAsync(tenantId: null, $"h{suffix}");
        var (tenantUserId, tenantNames) = await SeedUserWithRolesAsync(tenantId, $"t{suffix}");

        await using var scope = factory.Services.CreateAsyncScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // 另一租户用户的 Id 在本租户里查不到任何角色
        Assert.Empty(await userRepository.GetRoleNamesAsync(tenantUserId));
        using (currentTenant.Change(tenantId))
        {
            Assert.Equal([tenantNames.First, tenantNames.Second], await userRepository.GetRoleNamesAsync(tenantUserId));
            Assert.Empty(await userRepository.GetRoleNamesAsync(hostUserId));

            using (scope.ServiceProvider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>())
            {
                Assert.Equal([hostNames.First, hostNames.Second], await userRepository.GetRoleNamesAsync(hostUserId));
            }
        }
    }

    [Fact]
    public async Task Default_roles_only_come_from_the_current_tenant()
    {
        var suffix = NewSuffix();
        var tenantId = await NewTenantAsync();
        var otherTenantId = await NewTenantAsync();
        var names = await SeedRolesAsync(tenantId, suffix);
        await SeedRolesAsync(otherTenantId, $"o{suffix}");

        await using var scope = factory.Services.CreateAsyncScope();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        using (currentTenant.Change(tenantId))
        {
            var defaults = await roleRepository.GetDefaultRolesAsync();
            Assert.All(defaults, role => Assert.Equal(tenantId, role.TenantId));
            Assert.Equal(
                [names.First, names.Second],
                defaults.Select(role => role.Name).Where(name => name.EndsWith(suffix, StringComparison.Ordinal)));
        }

        Assert.DoesNotContain(
            await roleRepository.GetDefaultRolesAsync(),
            role => role.Name.EndsWith(suffix, StringComparison.Ordinal));
    }

#if (LocalIdentity)
    /// <summary>
    /// 本地身份下租户要先登记才能解析连接：经宿主管理员开通一个，开通会写入该租户自己的默认角色与管理员。
    /// </summary>
    private async Task<Guid> NewTenantAsync()
    {
        using var hostAdmin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var name = $"repo-{Guid.NewGuid():N}"[..20];
        using var response = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = name,
            DisplayName = name,
            AdminEmail = $"admin@{name}.example.test",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
#else
    /// <summary>资源服务不登记租户，任意新租户 Id 即可。</summary>
    private static Task<Guid> NewTenantAsync() => Task.FromResult(Guid.CreateVersion7());
#endif
#endif

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..10];

    private static User NewUser(string suffix) =>
#if (LocalIdentity)
        new($"repo_{suffix}", $"repo_{suffix}@example.test");
#else
        new(Guid.CreateVersion7(), $"repo_{suffix}", $"repo_{suffix}@example.test");
#endif

    /// <summary>
    /// 写入一个用户与四个角色关联：两个正常、一个角色已删除、一个关联已删除。
    /// 名称前缀故意与插入顺序相反，排序断言才有意义。
    /// </summary>
    private async Task<(Guid UserId, SeededNames Names)> SeedUserWithRolesAsync(Guid? tenantId, string suffix)
    {
        var names = new SeededNames($"b_{suffix}", $"c_{suffix}", $"d_{suffix}", $"e_{suffix}");
        await using var scope = factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId);
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var userRoleRepository = scope.ServiceProvider.GetRequiredService<IRepository<UserRole, Guid>>();
        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        User user;
        Role deletedRole;
        UserRole deletedAssignment;
        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            user = await userRepository.InsertAsync(NewUser(suffix));
            Role[] roles =
            [
                new(names.DeletedAssignment, "Deleted assignment"),
                new(names.DeletedRole, "Deleted role"),
                new(names.Second, "Second"),
                new(names.First, "First"),
            ];
            await roleRepository.InsertManyAsync(roles);
            var assignments = roles.Select(role => new UserRole(user.Id, role.Id)).ToList();
            await userRoleRepository.InsertManyAsync(assignments);
            await unitOfWork.CompleteAsync();
            deletedAssignment = assignments[0];
            deletedRole = roles[1];
        }

        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            await userRoleRepository.DeleteAsync(deletedAssignment);
            await roleRepository.DeleteAsync(deletedRole);
            await unitOfWork.CompleteAsync();
        }

        return (user.Id, names);
    }

    /// <summary>
    /// 写入四个角色：两个默认、一个默认但已删除、一个非默认。
    /// </summary>
    private async Task<SeededNames> SeedRolesAsync(Guid? tenantId, string suffix)
    {
        var names = new SeededNames($"b_{suffix}", $"c_{suffix}", $"d_{suffix}", $"e_{suffix}");
        await using var scope = factory.Services.CreateAsyncScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId);
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        var deletedRole = new Role(names.DeletedRole, "Deleted default", isDefault: true);
        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            await roleRepository.InsertManyAsync(
            [
                new Role(names.DeletedAssignment, "Not default"),
                deletedRole,
                new Role(names.Second, "Second default", isDefault: true),
                new Role(names.First, "First default", isDefault: true),
            ]);
            await unitOfWork.CompleteAsync();
        }

        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            await roleRepository.DeleteAsync(deletedRole);
            await unitOfWork.CompleteAsync();
        }

        return names;
    }

    /// <param name="First">排序在前的有效行。</param>
    /// <param name="Second">排序在后的有效行。</param>
    /// <param name="DeletedRole">已删除的角色。</param>
    /// <param name="DeletedAssignment">关联已删除的角色；默认角色用例里是非默认角色。</param>
    private sealed record SeededNames(string First, string Second, string DeletedRole, string DeletedAssignment);
}
