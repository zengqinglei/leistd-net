using Leistd.Authorization;
using Leistd.Authorization.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Leistd.Authorization.Tests;

public class EfCorePermissionGrantStoreManagerTests
{
    [Fact]
    public async Task GrantToUser_and_GrantToRole_create_grants_and_ignore_duplicates()
    {
        await using var db = CreateDbContext();
        var (store, manager) = CreateStoreAndManager(db);

        await manager.GrantToUserAsync("Orders.Read", "u1");
        await manager.GrantToUserAsync("Orders.Read", "u1");
        await manager.GrantToRoleAsync("Orders.Write", "r1");

        Assert.True(await store.IsGrantedToUserAsync("Orders.Read", "u1"));
        Assert.True(await store.IsGrantedToRoleAsync("Orders.Write", "r1"));
        Assert.False(await store.IsGrantedToUserAsync("Orders.Read", "u2"));
        Assert.Equal(2, await db.Set<PermissionGrantRecord>().CountAsync());
    }

    [Fact]
    public async Task Revoke_removes_only_matching_user_or_role_grant()
    {
        await using var db = CreateDbContext();
        var (store, manager) = CreateStoreAndManager(db);

        await manager.GrantToUserAsync("Orders.Read", "u1");
        await manager.GrantToUserAsync("Orders.Write", "u1");
        await manager.GrantToRoleAsync("Orders.Read", "r1");

        await manager.RevokeFromUserAsync("Orders.Read", "u1");
        await manager.RevokeFromRoleAsync("Orders.Read", "r1");

        Assert.False(await store.IsGrantedToUserAsync("Orders.Read", "u1"));
        Assert.True(await store.IsGrantedToUserAsync("Orders.Write", "u1"));
        Assert.False(await store.IsGrantedToRoleAsync("Orders.Read", "r1"));
        Assert.Equal(1, await db.Set<PermissionGrantRecord>().CountAsync());
    }

    [Fact]
    public async Task GetGrantedPermissions_returns_distinct_permissions_for_user_and_role()
    {
        await using var db = CreateDbContext();
        var (_, manager) = CreateStoreAndManager(db);

        await manager.GrantToUserAsync("Orders.Read", "u1");
        await manager.GrantToUserAsync("Orders.Write", "u1");
        await manager.GrantToUserAsync("Orders.Delete", "u2");
        await manager.GrantToRoleAsync("Orders.Approve", "r1");
        await manager.GrantToRoleAsync("Orders.Export", "r1");
        await manager.GrantToRoleAsync("Orders.Read", "r2");

        var userPermissions = await manager.GetGrantedPermissionsForUserAsync("u1");
        var rolePermissions = await manager.GetGrantedPermissionsForRoleAsync("r1");

        Assert.Equal(
            new HashSet<string>(["Orders.Read", "Orders.Write"], StringComparer.Ordinal),
            userPermissions);
        Assert.Equal(
            new HashSet<string>(["Orders.Approve", "Orders.Export"], StringComparer.Ordinal),
            rolePermissions);
    }

    [Fact]
    public async Task IsGrantedToUserOrRolesAsync_returns_result_for_each_requested_permission()
    {
        await using var db = CreateDbContext();
        var (store, manager) = CreateStoreAndManager(db);

        await manager.GrantToUserAsync("Orders.Read", "u1");
        await manager.GrantToRoleAsync("Orders.Write", "r1");
        await manager.GrantToRoleAsync("Orders.Delete", "r2");
        await manager.GrantToRoleAsync("Orders.Approve", "other-role");

        var result = await store.IsGrantedToUserOrRolesAsync(
            ["Orders.Read", "Orders.Write", "Orders.Delete", "Orders.Approve", "Orders.Missing", ""],
            "u1",
            ["r1", "r2"]);

        Assert.Equal(5, result.Count);
        Assert.True(result["Orders.Read"]);
        Assert.True(result["Orders.Write"]);
        Assert.True(result["Orders.Delete"]);
        Assert.False(result["Orders.Approve"]);
        Assert.False(result["Orders.Missing"]);
        Assert.False(result.ContainsKey(""));
    }

    [Fact]
    public async Task IsGrantedToAnyRoleAsync_returns_false_when_roles_are_empty_or_unmatched()
    {
        await using var db = CreateDbContext();
        var (store, manager) = CreateStoreAndManager(db);

        await manager.GrantToRoleAsync("Orders.Read", "r1");

        Assert.True(await store.IsGrantedToAnyRoleAsync("Orders.Read", ["r0", "r1"]));
        Assert.False(await store.IsGrantedToAnyRoleAsync("Orders.Read", []));
        Assert.False(await store.IsGrantedToAnyRoleAsync("Orders.Read", ["r2"]));
    }

    private static (EfCorePermissionGrantStore<TestAuthorizationDbContext> Store,
        EfCorePermissionGrantManager<TestAuthorizationDbContext> Manager) CreateStoreAndManager(
            TestAuthorizationDbContext dbContext)
    {
        var store = new EfCorePermissionGrantStore<TestAuthorizationDbContext>(dbContext);
        var manager = new EfCorePermissionGrantManager<TestAuthorizationDbContext>(dbContext, store);
        return (store, manager);
    }

    private static TestAuthorizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestAuthorizationDbContext>()
            .UseInMemoryDatabase($"permission-grants-{Guid.NewGuid()}")
            .Options;

        return new TestAuthorizationDbContext(options);
    }

    private sealed class TestAuthorizationDbContext(DbContextOptions<TestAuthorizationDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureAuthorization();
        }
    }
}
