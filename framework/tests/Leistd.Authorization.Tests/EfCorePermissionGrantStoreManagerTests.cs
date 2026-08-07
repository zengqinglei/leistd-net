using Leistd.Authorization.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Leistd.Authorization.Tests;

/// <summary>
/// 授予存储与管理器的关系型行为验证。
/// </summary>
/// <remarks>
/// 使用 Sqlite 而非 InMemory：只有关系型 Provider 才会真正强制唯一索引、真正翻译查询表达式，
/// InMemory 全部在内存求值，会让不可翻译的查询和被违反的唯一约束静默通过。
/// </remarks>
public class EfCorePermissionGrantStoreManagerTests : IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private TestAuthorizationDbContext _db = default!;
    private EfCorePermissionGrantStore<TestAuthorizationDbContext> _store = default!;
    private EfCorePermissionGrantManager<TestAuthorizationDbContext> _manager = default!;
    private PermissionDefinitionManager _definitions = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestAuthorizationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestAuthorizationDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _definitions = TestPermissionDefinitions.CreateManager();
        _store = new EfCorePermissionGrantStore<TestAuthorizationDbContext>(_db);
        _manager = new EfCorePermissionGrantManager<TestAuthorizationDbContext>(_db, _definitions);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Granting_a_child_completes_its_ancestors()
    {
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersWriteBatch,
            PermissionGrantProviderNames.Role,
            "r1");

        var grants = await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1");

        Assert.Equal(
            [
                TestPermissionDefinitionProvider.Orders,
                TestPermissionDefinitionProvider.OrdersWrite,
                TestPermissionDefinitionProvider.OrdersWriteBatch
            ],
            grants.Grants.Select(x => x.PermissionName).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(grants.Grants, grant => Assert.Equal(PermissionGrantEffect.Granted, grant.Effect));
    }

    [Fact]
    public async Task Revoking_a_parent_cascades_to_its_descendants()
    {
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersWriteBatch,
            PermissionGrantProviderNames.Role,
            "r1");
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersRead,
            PermissionGrantProviderNames.Role,
            "r1");

        await _manager.RevokeAsync(
            TestPermissionDefinitionProvider.OrdersWrite,
            PermissionGrantProviderNames.Role,
            "r1");

        var grants = await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1");

        Assert.Equal(
            [TestPermissionDefinitionProvider.Orders, TestPermissionDefinitionProvider.OrdersRead],
            grants.Grants.Select(x => x.PermissionName).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Prohibiting_a_parent_drops_granted_descendants()
    {
        var revision = await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [
                new PermissionGrant(TestPermissionDefinitionProvider.Orders, PermissionGrantEffect.Prohibited),
                new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted)
            ]);

        var grants = await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1");

        // 拒绝优先：被拒绝权限的子孙回落为未授予，运行时同样拒绝。
        var grant = Assert.Single(grants.Grants);
        Assert.Equal(TestPermissionDefinitionProvider.Orders, grant.PermissionName);
        Assert.Equal(PermissionGrantEffect.Prohibited, grant.Effect);
        Assert.Equal(1, revision);
    }

    [Fact]
    public async Task Prohibiting_a_child_keeps_the_granted_parent()
    {
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [
                new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted),
                new PermissionGrant(TestPermissionDefinitionProvider.OrdersDelete, PermissionGrantEffect.Prohibited)
            ]);

        var grants = (await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1"))
            .Grants
            .ToDictionary(x => x.PermissionName, x => x.Effect, StringComparer.Ordinal);

        Assert.Equal(PermissionGrantEffect.Granted, grants[TestPermissionDefinitionProvider.Orders]);
        Assert.Equal(PermissionGrantEffect.Granted, grants[TestPermissionDefinitionProvider.OrdersRead]);
        Assert.Equal(PermissionGrantEffect.Prohibited, grants[TestPermissionDefinitionProvider.OrdersDelete]);
    }

    [Fact]
    public async Task Replace_rejects_undefined_or_disabled_permissions()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.Undefined, PermissionGrantEffect.Granted)]));

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.ReportsView, PermissionGrantEffect.Granted)]));

        Assert.Empty((await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1")).Grants);
    }

    [Fact]
    public async Task Replace_bumps_revision_only_when_something_changed()
    {
        var first = await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted)]);

        var unchanged = await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted)]);

        var second = await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersWrite, PermissionGrantEffect.Granted)]);

        Assert.Equal(1, first);
        Assert.Equal(1, unchanged);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task Replace_with_stale_revision_is_rejected_instead_of_overwriting()
    {
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted)],
            expectedRevision: 0);

        var exception = await Assert.ThrowsAsync<PermissionGrantConcurrencyException>(
            () => _manager.ReplaceGrantsAsync(
                PermissionGrantProviderNames.Role,
                "r1",
                [new PermissionGrant(TestPermissionDefinitionProvider.OrdersDelete, PermissionGrantEffect.Granted)],
                expectedRevision: 0));

        Assert.Equal(0, exception.ExpectedRevision);
        Assert.Equal(1, exception.ActualRevision);

        // 冲突时不得静默覆盖：原授予保持不变。
        var grants = await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1");
        Assert.Contains(grants.Grants, x => x.PermissionName == TestPermissionDefinitionProvider.OrdersRead);
        Assert.DoesNotContain(grants.Grants, x => x.PermissionName == TestPermissionDefinitionProvider.OrdersDelete);
    }

    [Fact]
    public async Task Subject_grants_are_fetched_for_user_and_all_roles_at_once()
    {
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersRead,
            PermissionGrantProviderNames.User,
            "u1");
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersWrite,
            PermissionGrantProviderNames.Role,
            "r1");
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersDelete,
            PermissionGrantProviderNames.Role,
            "other-role");

        var subject = await _store.GetGrantsForSubjectAsync("u1", ["r1", "r2"]);

        Assert.Contains(subject.UserGrants.Grants, x => x.PermissionName == TestPermissionDefinitionProvider.OrdersRead);
        Assert.Equal(2, subject.RoleGrants.Count);

        var effects = subject.GetEffectiveEffects(_definitions);
        Assert.Equal(PermissionGrantEffect.Granted, effects[TestPermissionDefinitionProvider.OrdersRead]);
        Assert.Equal(PermissionGrantEffect.Granted, effects[TestPermissionDefinitionProvider.OrdersWrite]);
        // 未参与的角色的授予不得泄漏到本主体。
        Assert.False(effects.ContainsKey(TestPermissionDefinitionProvider.OrdersDelete));
    }

    [Fact]
    public async Task Subject_revision_changes_with_grants_and_with_role_membership()
    {
        var before = (await _store.GetGrantsForSubjectAsync("u1", ["r1"])).Revision;

        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersRead,
            PermissionGrantProviderNames.Role,
            "r1");
        var afterGrant = (await _store.GetGrantsForSubjectAsync("u1", ["r1"])).Revision;

        var afterMembership = (await _store.GetGrantsForSubjectAsync("u1", ["r1", "r2"])).Revision;

        Assert.NotEqual(before, afterGrant);
        Assert.NotEqual(afterGrant, afterMembership);
    }

    [Fact]
    public async Task Unique_index_rejects_duplicate_grant_rows()
    {
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersRead,
            PermissionGrantProviderNames.Role,
            "r1");

        _db.Set<PermissionGrantRecord>().Add(new PermissionGrantRecord
        {
            PermissionName = TestPermissionDefinitionProvider.OrdersRead,
            ProviderName = PermissionGrantProviderNames.Role,
            ProviderKey = "r1",
            Effect = PermissionGrantEffect.Prohibited
        });

        // 同一主体对同一权限不可能同时存在允许与拒绝两行。
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Granting_the_same_permission_twice_updates_in_place()
    {
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersRead,
            PermissionGrantProviderNames.Role,
            "r1");
        await _manager.GrantAsync(
            TestPermissionDefinitionProvider.OrdersRead,
            PermissionGrantProviderNames.Role,
            "r1",
            PermissionGrantEffect.Prohibited);

        var grants = (await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1")).Grants;

        var read = Assert.Single(grants, x => x.PermissionName == TestPermissionDefinitionProvider.OrdersRead);
        Assert.Equal(PermissionGrantEffect.Prohibited, read.Effect);
    }

    [Theory]
    // 用户拒绝父权限、角色允许子权限。
    [InlineData(PermissionGrantProviderNames.User, PermissionGrantProviderNames.Role)]
    // 角色拒绝父权限、用户允许子权限。
    [InlineData(PermissionGrantProviderNames.Role, PermissionGrantProviderNames.User)]
    public async Task Prohibiting_a_parent_in_one_source_disables_children_granted_by_another(
        string prohibitingProvider,
        string grantingProvider)
    {
        var prohibitingKey = prohibitingProvider == PermissionGrantProviderNames.User ? "u1" : "r1";
        var grantingKey = grantingProvider == PermissionGrantProviderNames.User ? "u1" : "r1";

        await _manager.ReplaceGrantsAsync(
            prohibitingProvider,
            prohibitingKey,
            [new PermissionGrant(TestPermissionDefinitionProvider.Orders, PermissionGrantEffect.Prohibited)]);
        await _manager.ReplaceGrantsAsync(
            grantingProvider,
            grantingKey,
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersWrite, PermissionGrantEffect.Granted)]);

        var effects = (await _store.GetGrantsForSubjectAsync("u1", ["r1"]))
            .GetEffectiveEffects(_definitions);

        // 写入侧的归一化只在单个主体内成立；跨来源合并后必须由读取侧把拒绝继续传播下去。
        Assert.Equal(PermissionGrantEffect.Prohibited, effects[TestPermissionDefinitionProvider.Orders]);
        Assert.Equal(PermissionGrantEffect.Prohibited, effects[TestPermissionDefinitionProvider.OrdersWrite]);
    }

    [Fact]
    public async Task Prohibiting_a_parent_in_one_role_disables_children_granted_by_another_role()
    {
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.Orders, PermissionGrantEffect.Prohibited)]);
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r2",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted)]);

        var effects = (await _store.GetGrantsForSubjectAsync("u1", ["r1", "r2"]))
            .GetEffectiveEffects(_definitions);

        Assert.Equal(PermissionGrantEffect.Prohibited, effects[TestPermissionDefinitionProvider.OrdersRead]);
    }

    [Fact]
    public async Task Prohibition_propagates_through_every_level_of_ancestors()
    {
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.Orders, PermissionGrantEffect.Prohibited)]);
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r2",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersWriteBatch, PermissionGrantEffect.Granted)]);

        var effects = (await _store.GetGrantsForSubjectAsync("u1", ["r1", "r2"]))
            .GetEffectiveEffects(_definitions);

        // 被拒绝的是祖父级，孙子级同样必须失效。
        Assert.Equal(PermissionGrantEffect.Prohibited, effects[TestPermissionDefinitionProvider.OrdersWriteBatch]);
    }

    [Fact]
    public async Task Interleaved_writes_on_the_same_subject_lose_the_optimistic_concurrency_check()
    {
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted)]);

        var options = new DbContextOptionsBuilder<TestAuthorizationDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var firstDb = new TestAuthorizationDbContext(options);
        await using var secondDb = new TestAuthorizationDbContext(options);
        var first = new EfCorePermissionGrantManager<TestAuthorizationDbContext>(firstDb, _definitions);
        var second = new EfCorePermissionGrantManager<TestAuthorizationDbContext>(secondDb, _definitions);

        // 两个上下文都把版本读进内存后才发生写入：这正是「先读后比」保护不了的窗口。
        var revision = (await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1")).Revision;
        await firstDb.Set<AuthorizationRevisionRecord>().AsTracking().ToListAsync();
        await secondDb.Set<AuthorizationRevisionRecord>().AsTracking().ToListAsync();

        await first.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersWrite, PermissionGrantEffect.Granted)],
            revision);

        // 第二个上下文持有的仍是写入前的版本行，提交时必须失败而不是覆盖先写。
        var conflict = await Assert.ThrowsAsync<PermissionGrantConcurrencyException>(
            () => second.ReplaceGrantsAsync(
                PermissionGrantProviderNames.Role,
                "r1",
                [new PermissionGrant(TestPermissionDefinitionProvider.OrdersDelete, PermissionGrantEffect.Granted)],
                revision));

        Assert.Equal(PermissionGrantProviderNames.Role, conflict.ProviderName);

        // 先写的内容原样保留。
        var grants = (await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, "r1")).Grants;
        Assert.Contains(grants, x => x.PermissionName == TestPermissionDefinitionProvider.OrdersWrite);
        Assert.DoesNotContain(grants, x => x.PermissionName == TestPermissionDefinitionProvider.OrdersDelete);
    }

    [Fact]
    public async Task Grants_for_many_subjects_are_fetched_in_a_constant_number_of_round_trips()
    {
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r1",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted)]);
        await _manager.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            "r2",
            [new PermissionGrant(TestPermissionDefinitionProvider.OrdersWriteBatch, PermissionGrantEffect.Granted)]);

        var sets = await _store.GetGrantsAsync(PermissionGrantProviderNames.Role, ["r1", "r2", "r3"]);

        Assert.Equal(3, sets.Count);
        Assert.Equal(["r1", "r2", "r3"], sets.Select(x => x.ProviderKey));
        // r2 授予了孙子级，写入归一化补齐了两级祖先。
        Assert.Equal(3, sets.Single(x => x.ProviderKey == "r2").Grants.Count);
        // 从未写入过的主体返回空集合与 0 版本，而不是被跳过。
        var missing = sets.Single(x => x.ProviderKey == "r3");
        Assert.Empty(missing.Grants);
        Assert.Equal(0, missing.Revision);
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
