using static Leistd.TestBase.DbContextProviderFor;
using System.Data.Common;
using Leistd.Authorization.Resource.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Permissions;
using Leistd.Authorization.Resource.EntityFrameworkCore.Managers;
using Leistd.Authorization.Resource.EntityFrameworkCore.Stores;
using Leistd.Authorization.Resource.Grants;
using Leistd.Authorization.Resource.Services;
using Leistd.Authorization.Exceptions;
using Leistd.Authorization.Resource.EntityFrameworkCore.Entities;
using Leistd.Authorization.Resource.Exceptions;
using Leistd.Authorization.Resource.Abstractions;

namespace Leistd.Authorization.Resource.Tests;

public class ResourceAuthorizationTests : IAsyncLifetime
{
    private const string OwnerUserId = "u-owner";
    private const string OtherUserId = "u-other";

    private SqliteConnection _connection = default!;
    private TestOrderDbContext _db = default!;
    private EfCoreResourceGrantStore<TestOrderDbContext> _store = default!;
    private EfCoreResourceGrantManager<TestOrderDbContext> _manager = default!;
    private TestOrder _ownOrder = default!;
    private TestOrder _otherOrder = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestOrderDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestOrderDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _store = new EfCoreResourceGrantStore<TestOrderDbContext>(Fixed(_db));
        _manager = new EfCoreResourceGrantManager<TestOrderDbContext>(Fixed(_db));

        _ownOrder = new TestOrder
        {
            ResourceKey = "order-1",
            OwnerId = OwnerUserId,
            OrganizationId = "org-1",
            Title = "自己的订单"
        };
        _otherOrder = new TestOrder
        {
            ResourceKey = "order-2",
            OwnerId = OtherUserId,
            OrganizationId = "org-2",
            Title = "别人的订单"
        };

        _db.Orders.AddRange(_ownOrder, _otherOrder);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Denies_by_default_when_no_rule_and_no_acl_gives_a_verdict()
    {
        var service = CreateService(Subject(OwnerUserId));

        Assert.False(await service.IsGrantedAsync(_ownOrder, ResourceOperations.Read));
    }

    [Fact]
    public async Task Owner_rule_allows_only_the_owned_instance()
    {
        var service = CreateService(Subject(OwnerUserId), withOwnerHandler: true);

        Assert.True(await service.IsGrantedAsync(_ownOrder, ResourceOperations.Read));
        Assert.False(await service.IsGrantedAsync(_otherOrder, ResourceOperations.Read));
    }

    [Fact]
    public async Task Acl_grant_allows_an_instance_the_rules_do_not_cover()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _otherOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Granted)
        ]);

        var service = CreateService(Subject(OwnerUserId));

        Assert.True(await service.IsGrantedAsync(_otherOrder, ResourceOperations.Read));
        Assert.False(await service.IsGrantedAsync(_otherOrder, ResourceOperations.Update));
    }

    [Fact]
    public async Task Acl_deny_beats_an_allowing_rule()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Prohibited)
        ]);

        var service = CreateService(Subject(OwnerUserId), withOwnerHandler: true);

        Assert.False(await service.IsGrantedAsync(_ownOrder, ResourceOperations.Read));
    }

    [Fact]
    public async Task Rule_deny_beats_an_allowing_acl()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Delete, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Granted)
        ]);

        var service = CreateService(Subject(OwnerUserId), withOwnerHandler: true, withArchivedDenyHandler: true);

        Assert.False(await service.IsGrantedAsync(_ownOrder, ResourceOperations.Delete));
    }

    [Fact]
    public async Task Super_admin_bypasses_resource_authorization()
    {
        var service = CreateService(new PermissionSubject(OtherUserId, [], IsSuperAdmin: true));

        Assert.True(await service.IsGrantedAsync(_ownOrder, ResourceOperations.Delete));
    }

    [Fact]
    public async Task Unauthenticated_subject_is_denied()
    {
        var service = CreateService(null, withOwnerHandler: true);

        Assert.False(await service.IsGrantedAsync(_ownOrder, ResourceOperations.Read));
    }

    [Fact]
    public async Task Acl_is_merged_into_the_collection_query_instead_of_per_row_checks()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _otherOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.Role, "r1",
                ResourceGrantEffect.Granted)
        ]);

        var grantedKeys = await _store.QueryGrantedResourceKeysAsync(
            TestOrder.Resource,
            ResourceOperations.Read,
            OwnerUserId,
            ["r1"]);

        var query = _db.Orders.Where(order => grantedKeys.Contains(order.ResourceKey));

        // 由数据库执行：EF Core 会对无法翻译的表达式抛异常，因此这里通过即证明生成了 SQL 子查询。
        var count = await query.CountAsync();
        var page = await query.OrderBy(x => x.ResourceKey).Take(10).ToListAsync();

        Assert.Equal(1, count);
        Assert.Equal(_otherOrder.ResourceKey, Assert.Single(page).ResourceKey);
    }

    [Fact]
    public async Task Explicit_deny_is_excluded_from_the_collection_query()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _otherOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.Role, "r1",
                ResourceGrantEffect.Granted),
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Prohibited)
        ]);

        var grantedKeys = await _store.QueryGrantedResourceKeysAsync(
            TestOrder.Resource,
            ResourceOperations.Read,
            OwnerUserId,
            ["r1"]);

        Assert.Empty(await _db.Orders.Where(order => grantedKeys.Contains(order.ResourceKey)).ToListAsync());
    }

    [Fact]
    public async Task Other_subjects_acl_does_not_leak_into_the_collection_query()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _otherOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);

        var grantedKeys = await _store.QueryGrantedResourceKeysAsync(
            TestOrder.Resource,
            ResourceOperations.Read,
            OwnerUserId,
            []);

        Assert.Empty(await _db.Orders.Where(order => grantedKeys.Contains(order.ResourceKey)).ToListAsync());
    }

    [Fact]
    public async Task Removing_a_resource_clears_its_acl_and_is_idempotent()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Granted),
            new ResourceGrant(ResourceOperations.Update, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Granted)
        ]);

        Assert.Equal(2, await _manager.RemoveResourceAsync(TestOrder.Resource, _ownOrder.ResourceKey));
        Assert.Equal(0, await _manager.RemoveResourceAsync(TestOrder.Resource, _ownOrder.ResourceKey));
        Assert.Empty((await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey)).Grants);
    }

    [Fact]
    public async Task A_super_admin_cannot_break_a_domain_rule()
    {
        var service = CreateService(
            new PermissionSubject("u-root", [], IsSuperAdmin: true),
            withArchivedDenyHandler: true);

        // 超管旁路的是授权侧判定（RBAC、数据范围、ACL 缺失、默认拒绝），
        // 不是领域不变量。"已归档的订单谁都不能删"这类规则由资源状态决定，与是谁无关；
        // 让超管跳过 Handler 会让组件文档当场说谎。
        Assert.False(await service.IsGrantedAsync(
            _ownOrder, TestOrder.Resource, _ownOrder.ResourceKey, ResourceOperations.Delete));

        var withoutRule = CreateService(new PermissionSubject("u-root", [], IsSuperAdmin: true));
        Assert.True(await withoutRule.IsGrantedAsync(
            _otherOrder, TestOrder.Resource, _otherOrder.ResourceKey, ResourceOperations.Read));
    }

    [Theory]
    [InlineData("Rloe", "u-1", ResourceOperations.Read)]
    [InlineData(PermissionGrantProviderNames.User, "", ResourceOperations.Read)]
    [InlineData(PermissionGrantProviderNames.User, "u-1", "")]
    public async Task Unaddressable_grant_subjects_are_rejected_on_write(
        string providerName,
        string providerKey,
        string operation)
    {
        // 读取端只按 User/Role 匹配主体：写进别的 ProviderName 或空标识的记录既不放行也不拒绝，
        // 只是永远匹配不上，成为查不出原因的脏数据。
        await Assert.ThrowsAsync<InvalidResourceGrantException>(
            () => _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
            [
                new ResourceGrant(operation, providerName, providerKey, ResourceGrantEffect.Granted)
            ]));

        Assert.Empty((await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey)).Grants);
    }

    [Fact]
    public async Task A_never_settling_snapshot_fails_as_a_read_error()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);

        var options = new DbContextOptionsBuilder<TestOrderDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new BumpVersionAfterEachReadInterceptor(_connection))
            .Options;

        await using var db = new TestOrderDbContext(options);
        var store = new EfCoreResourceGrantStore<TestOrderDbContext>(Fixed(db));

        var failure = await Assert.ThrowsAsync<UnstableGrantSnapshotException>(
            () => store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey));

        Assert.Contains(_ownOrder.ResourceKey, failure.Subject);
    }

    /// <summary>每次读到资源版本表之后，用另一条连接把版本推高一格。</summary>
    private sealed class BumpVersionAfterEachReadInterceptor(SqliteConnection connection) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("ResourceAuthorizationVersions", StringComparison.Ordinal))
                return result;

            await using var bump = connection.CreateCommand();
            bump.CommandText = "UPDATE \"ResourceAuthorizationVersions\" SET \"Version\" = \"Version\" + 1";
            await bump.ExecuteNonQueryAsync(cancellationToken);

            return result;
        }
    }

    [Fact]
    public async Task Undefined_grant_effects_are_rejected_on_write()
    {
        // 枚举在 .NET 里能承载任意底层值，(ResourceGrantEffect)999 是合法表达式。
        // 判定端 fail-closed 之后它会让整个资源变成"谁都不许访问"，因此必须在写入口拒掉。
        var invalid = await Assert.ThrowsAsync<InvalidResourceGrantException>(
            () => _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
            [
                new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                    (ResourceGrantEffect)999)
            ]));

        Assert.Equal(TestOrder.Resource, invalid.ResourceName);
        // 两类非法写入合并为一个异常类型后，"是哪一类"由 Reason 承载，这里钉住它
        Assert.Contains("999", invalid.Reason, StringComparison.Ordinal);

        var set = await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey);
        Assert.Empty(set.Grants);
        Assert.Equal(0, set.Version);
    }

    [Fact]
    public async Task A_corrupted_effect_from_the_store_denies_instead_of_allowing()
    {
        var service = new DefaultResourceAuthorizationService(
            new FakeSubjectProvider(Subject(OtherUserId)),
            new ServiceCollection().BuildServiceProvider(),
            new CorruptedEffectStore());

        Assert.False(await service.IsGrantedAsync(
            _otherOrder, TestOrder.Resource, _otherOrder.ResourceKey, ResourceOperations.Read));
    }

    [Fact]
    public async Task Replacing_acl_with_a_stale_version_is_rejected()
    {
        var stale = await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey);

        // A：把某人显式排除在外。
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.Role, "r-review",
                ResourceGrantEffect.Granted),
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Prohibited)
        ], stale.Version);

        // B：基于 A 之前的旧页面保存，若不校验版本，A 刚加的显式拒绝会被静默抹掉，
        // 被排除的人重新经由角色拿到访问权，而两次保存都会显示成功。
        var conflict = await Assert.ThrowsAsync<ResourceGrantConcurrencyException>(
            () => _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
            [
                new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.Role, "r-review",
                    ResourceGrantEffect.Granted)
            ], stale.Version));

        Assert.Equal(stale.Version, conflict.ExpectedVersion);
        Assert.Equal(1, conflict.ActualVersion);

        var current = await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey);
        Assert.Contains(current.Grants, x =>
            x.ProviderKey == OtherUserId && x.Effect == ResourceGrantEffect.Prohibited);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_corrupted_effect_stays_sticky_regardless_of_row_order(bool corruptedFirst)
    {
        // 直接写库绕过 Manager 校验，模拟旧数据或未应用约束的库。
        var corrupted = new ResourcePermissionGrantRecord
        {
            ResourceName = TestOrder.Resource,
            ResourceKey = _otherOrder.ResourceKey,
            Operation = ResourceOperations.Read,
            ProviderName = PermissionGrantProviderNames.Role,
            ProviderKey = "r-corrupt",
            Effect = (ResourceGrantEffect)999
        };

        var granted = new ResourcePermissionGrantRecord
        {
            ResourceName = TestOrder.Resource,
            ResourceKey = _otherOrder.ResourceKey,
            Operation = ResourceOperations.Read,
            ProviderName = PermissionGrantProviderNames.User,
            ProviderKey = OtherUserId,
            Effect = ResourceGrantEffect.Granted
        };

        // 聚合若只把 Prohibited 当粘滞值，损坏值会被后来的 Granted 覆盖成明确放行，
        // 而 SQL 没有排序，结果还会随执行计划漂移——两种顺序都必须拒绝。
        //
        // 临时关闭 CHECK 以模拟"约束尚未应用的旧库"：新写入已被 Manager 校验与数据库约束双重挡下，
        // 但存量数据里可能早就躺着这种值，读取端仍必须自己站稳。
        await _db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");
        _db.AddRange(corruptedFirst ? [corrupted, granted] : new[] { granted, corrupted });
        await _db.SaveChangesAsync();
        await _db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF;");
        _db.ChangeTracker.Clear();

        var effects = await _store.GetEffectiveGrantsAsync(
            TestOrder.Resource, _otherOrder.ResourceKey, OtherUserId, ["r-corrupt"]);

        Assert.NotEqual(ResourceGrantEffect.Granted, effects[ResourceOperations.Read]);

        var service = CreateService(Subject(OtherUserId, "r-corrupt"));
        Assert.False(await service.IsGrantedAsync(
            _otherOrder, TestOrder.Resource, _otherOrder.ResourceKey, ResourceOperations.Read));

        // 集合查询必须与单实例判定同口径：只在这里放行，列表、分页、统计、导出就会把
        // 单条判定明确拒绝的资源照样列出来。
        var grantedKeyQuery = await _store
            .QueryGrantedResourceKeysAsync(TestOrder.Resource, ResourceOperations.Read, OtherUserId, ["r-corrupt"]);
        var grantedKeys = await grantedKeyQuery.ToListAsync();

        Assert.DoesNotContain(_otherOrder.ResourceKey, grantedKeys);
    }

    [Fact]
    public async Task Removing_a_resource_also_clears_its_version()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);

        Assert.Equal(1, (await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey)).Version);

        await _manager.RemoveResourceAsync(TestOrder.Resource, _ownOrder.ResourceKey);

        // 版本行留着会持续累积孤儿记录，资源 Key 被重用时新资源还会继承上一任的版本号。
        var after = await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey);
        Assert.Empty(after.Grants);
        Assert.Equal(0, after.Version);
    }

    [Fact]
    public async Task A_conflicted_effect_change_is_rolled_back_in_the_host_context()
    {
        var options = new DbContextOptionsBuilder<TestOrderDbContext>().UseSqlite(_connection).Options;

        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);

        // 冲突必须发生在 SaveChanges 上：传一个过期的 expectedVersion 会在内存检查阶段就抛出，
        // 那时还没动过任何实体，回滚路径根本走不到。
        var interceptor = new RunOnceBeforeSaveInterceptor(async () =>
        {
            await using var winnerDb = new TestOrderDbContext(options);
            var winner = new EfCoreResourceGrantManager<TestOrderDbContext>(Fixed(winnerDb));
            await winner.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
            [
                new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                    ResourceGrantEffect.Granted),
                new ResourceGrant(ResourceOperations.Update, PermissionGrantProviderNames.User, OtherUserId,
                    ResourceGrantEffect.Granted)
            ]);
        });

        var loserOptions = new DbContextOptionsBuilder<TestOrderDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        await using var loserDb = new TestOrderDbContext(loserOptions);
        var loser = new EfCoreResourceGrantManager<TestOrderDbContext>(Fixed(loserDb));
        var loserStore = new EfCoreResourceGrantStore<TestOrderDbContext>(Fixed(loserDb));

        var snapshot = await loserStore.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey);

        await Assert.ThrowsAsync<ResourceGrantConcurrencyException>(
            () => loser.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
            [
                new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                    ResourceGrantEffect.Prohibited)
            ], snapshot.Version));

        // 冲突之后跟踪器里不得残留"从未落库的 Prohibited"：同一 scoped DbContext 后续的 tracking
        // 查询会拿到它，重试时甚至会判成"没有变化"而跳过写入。
        // 还原由 EF 在 Modified → Unchanged 转换时完成，这条用例把该保证钉住。
        var tracked = await loserDb.Set<ResourcePermissionGrantRecord>()
            .Where(x => x.ResourceName == TestOrder.Resource
                        && x.ResourceKey == _ownOrder.ResourceKey
                        && x.Operation == ResourceOperations.Read)
            .SingleAsync();

        Assert.Equal(ResourceGrantEffect.Granted, tracked.Effect);
    }

    /// <summary>在被拦截上下文的首次 SaveChanges 之前执行一次给定动作，用于构造确定性的写入交错。</summary>
    private sealed class RunOnceBeforeSaveInterceptor(Func<Task> action) : SaveChangesInterceptor
    {
        private bool executed;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!executed)
            {
                executed = true;
                await action();
            }

            return result;
        }
    }

    /// <summary>返回损坏效果值的 Store，用于验证判定端 fail-closed。</summary>
    private sealed class CorruptedEffectStore : IResourceGrantStore
    {
        public Task<ResourceGrantSet> GetGrantsAsync(
            string resourceName,
            string resourceKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ResourceGrantSet(resourceName, resourceKey, [], 0));

        public Task<IReadOnlyDictionary<string, ResourceGrantEffect>> GetEffectiveGrantsAsync(
            string resourceName,
            string resourceKey,
            string userId,
            IReadOnlyCollection<string> roleIds,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, ResourceGrantEffect> effects = new Dictionary<string, ResourceGrantEffect>
            {
                [ResourceOperations.Read] = (ResourceGrantEffect)999
            };

            return Task.FromResult(effects);
        }

        public Task<IQueryable<string>> QueryGrantedResourceKeysAsync(
            string resourceName,
            string operation,
            string userId,
            IReadOnlyCollection<string> roleIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<string>().AsQueryable());

        public Task<IQueryable<string>> QueryDeniedResourceKeysAsync(
            string resourceName,
            string operation,
            string userId,
            IReadOnlyCollection<string> roleIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<string>().AsQueryable());
    }

    [Fact]
    public async Task Unique_index_rejects_duplicate_acl_rows()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Granted)
        ]);

        _db.Set<ResourcePermissionGrantRecord>().Add(new ResourcePermissionGrantRecord
        {
            ResourceName = TestOrder.Resource,
            ResourceKey = _ownOrder.ResourceKey,
            Operation = ResourceOperations.Read,
            ProviderName = PermissionGrantProviderNames.User,
            ProviderKey = OwnerUserId,
            Effect = ResourceGrantEffect.Prohibited
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
    }

    private static PermissionSubject Subject(string userId, params string[] roleIds)
        => new(userId, roleIds, IsSuperAdmin: false);

    private DefaultResourceAuthorizationService CreateService(
        PermissionSubject? subject,
        bool withOwnerHandler = false,
        bool withArchivedDenyHandler = false)
    {
        var services = new ServiceCollection();

        if (withOwnerHandler)
        {
            services.AddSingleton<IResourceAuthorizationHandler<TestOrder>, OwnerHandler>();
        }

        if (withArchivedDenyHandler)
        {
            services.AddSingleton<IResourceAuthorizationHandler<TestOrder>, NoDeleteHandler>();
        }

        return new DefaultResourceAuthorizationService(
            new FakeSubjectProvider(subject),
            services.BuildServiceProvider(),
            _store);
    }

    private sealed class OwnerHandler : IResourceAuthorizationHandler<TestOrder>
    {
        public ValueTask HandleAsync(
            ResourceAuthorizationContext<TestOrder> context,
            CancellationToken cancellationToken = default)
        {
            if (context.Resource.OwnerId == context.Subject.UserId)
            {
                context.Allow();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoDeleteHandler : IResourceAuthorizationHandler<TestOrder>
    {
        public ValueTask HandleAsync(
            ResourceAuthorizationContext<TestOrder> context,
            CancellationToken cancellationToken = default)
        {
            if (context.Operation == ResourceOperations.Delete)
            {
                context.Deny();
            }

            return ValueTask.CompletedTask;
        }
    }


    [Fact]
    public async Task Removing_a_provider_clears_its_acl_across_every_resource()
    {
        // 缺这个入口时删除用户不只留孤儿行：主体标识被重用后旧 ACL 会重新生效——
        // 新用户拿到旧用户被分享过的资源，既不报错也不出现在任何审计里
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _otherOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted),
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Granted)
        ]);

        var removed = await _manager.RemoveProviderAsync(
            PermissionGrantProviderNames.User, OtherUserId);

        Assert.Equal(2, removed);

        Assert.Empty((await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey)).Grants);
        var otherAcl = await _store.GetGrantsAsync(TestOrder.Resource, _otherOrder.ResourceKey);
        Assert.Single(otherAcl.Grants);

        Assert.Equal(OwnerUserId, otherAcl.Grants[0].ProviderKey);
    }

    [Fact]
    public async Task Removing_a_provider_advances_the_version_of_affected_resources()
    {
        // 内容变了而版本不变，等于让持有旧集合的编辑者通过并发校验、把刚删掉的授予又写回去
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);
        var before = (await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey)).Version;

        await _manager.RemoveProviderAsync(PermissionGrantProviderNames.User, OtherUserId);

        var after = (await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey)).Version;
        Assert.True(after > before, $"version must advance: {before} -> {after}");

        await Assert.ThrowsAsync<ResourceGrantConcurrencyException>(() =>
            _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey, [], before));
    }

    [Fact]
    public async Task Removing_a_provider_only_advances_the_version_of_the_resource_it_touched()
    {
        // 版本行按「资源名 + 资源 Key」定位，而 ResourceKey 在不同资源名下可以重复。
        // 若批量查询用扁平的 key 集合下推，就会捞到另一个资源类型上同名 key 的版本行，
        // 把无关资源的版本一并推高——那些资源的编辑者会凭空撞 409，且查不出原因。
        const string OtherResourceName = "Invoices";
        var sharedKey = _ownOrder.ResourceKey;

        await _manager.ReplaceGrantsAsync(TestOrder.Resource, sharedKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);
        await _manager.ReplaceGrantsAsync(OtherResourceName, sharedKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                ResourceGrantEffect.Granted)
        ]);

        var untouchedBefore = (await _store.GetGrantsAsync(OtherResourceName, sharedKey)).Version;

        await _manager.RemoveProviderAsync(PermissionGrantProviderNames.User, OtherUserId);

        var untouchedAfter = await _store.GetGrantsAsync(OtherResourceName, sharedKey);
        Assert.Equal(untouchedBefore, untouchedAfter.Version);
        Assert.Single(untouchedAfter.Grants);

        await _manager.ReplaceGrantsAsync(OtherResourceName, sharedKey, [], untouchedBefore);
    }

    [Fact]
    public async Task Removing_a_provider_is_idempotent()
    {
        Assert.Equal(0, await _manager.RemoveProviderAsync(
            PermissionGrantProviderNames.User, "u-never-granted"));

        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OtherUserId,
                ResourceGrantEffect.Granted)
        ]);

        Assert.Equal(1, await _manager.RemoveProviderAsync(
            PermissionGrantProviderNames.User, OtherUserId));
        Assert.Equal(0, await _manager.RemoveProviderAsync(
            PermissionGrantProviderNames.User, OtherUserId));
    }

    [Fact]
    public async Task Removing_a_provider_rejects_blank_identifiers()
    {
        // 空白标识会命中一批本不该动的行；框架公共 API 的边界守卫
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _manager.RemoveProviderAsync(PermissionGrantProviderNames.User, "  "));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _manager.RemoveProviderAsync("  ", OtherUserId));
    }
}
