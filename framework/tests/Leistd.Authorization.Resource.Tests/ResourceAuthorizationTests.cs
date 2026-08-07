using Leistd.Authorization.Resource.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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

        _store = new EfCoreResourceGrantStore<TestOrderDbContext>(_db);
        _manager = new EfCoreResourceGrantManager<TestOrderDbContext>(_db);

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
        // 水平越权：同一个操作对别人的实例必须被拒绝。
        Assert.False(await service.IsGrantedAsync(_otherOrder, ResourceOperations.Read));
    }

    [Fact]
    public async Task Acl_grant_allows_an_instance_the_rules_do_not_cover()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _otherOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                PermissionGrantEffect.Granted)
        ]);

        var service = CreateService(Subject(OwnerUserId));

        Assert.True(await service.IsGrantedAsync(_otherOrder, ResourceOperations.Read));
        // 只授予了读，改仍然被拒绝。
        Assert.False(await service.IsGrantedAsync(_otherOrder, ResourceOperations.Update));
    }

    [Fact]
    public async Task Acl_deny_beats_an_allowing_rule()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                PermissionGrantEffect.Prohibited)
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
                PermissionGrantEffect.Granted)
        ]);

        // 领域规则：已归档的订单一律不可删除，即使有 ACL。
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
                PermissionGrantEffect.Granted)
        ]);

        var grantedKeys = _store.QueryGrantedResourceKeys(
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
                PermissionGrantEffect.Granted),
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                PermissionGrantEffect.Prohibited)
        ]);

        var grantedKeys = _store.QueryGrantedResourceKeys(
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
                PermissionGrantEffect.Granted)
        ]);

        var grantedKeys = _store.QueryGrantedResourceKeys(
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
                PermissionGrantEffect.Granted),
            new ResourceGrant(ResourceOperations.Update, PermissionGrantProviderNames.User, OwnerUserId,
                PermissionGrantEffect.Granted)
        ]);

        Assert.Equal(2, await _manager.RemoveResourceAsync(TestOrder.Resource, _ownOrder.ResourceKey));
        Assert.Equal(0, await _manager.RemoveResourceAsync(TestOrder.Resource, _ownOrder.ResourceKey));
        Assert.Empty(await _store.GetGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey));
    }

    [Fact]
    public async Task Unique_index_rejects_duplicate_acl_rows()
    {
        await _manager.ReplaceGrantsAsync(TestOrder.Resource, _ownOrder.ResourceKey,
        [
            new ResourceGrant(ResourceOperations.Read, PermissionGrantProviderNames.User, OwnerUserId,
                PermissionGrantEffect.Granted)
        ]);

        _db.Set<ResourcePermissionGrantRecord>().Add(new ResourcePermissionGrantRecord
        {
            ResourceName = TestOrder.Resource,
            ResourceKey = _ownOrder.ResourceKey,
            Operation = ResourceOperations.Read,
            ProviderName = PermissionGrantProviderNames.User,
            ProviderKey = OwnerUserId,
            Effect = PermissionGrantEffect.Prohibited
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
}
