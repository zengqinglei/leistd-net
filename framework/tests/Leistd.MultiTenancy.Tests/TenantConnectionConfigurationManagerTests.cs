using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.Exceptions;

namespace Leistd.MultiTenancy.Tests;

public class TenantConnectionConfigurationManagerTests : IAsyncLifetime
{
    private ServiceProvider _provider = default!;
    private SqliteConnection _connection = default!;
    private TestDbContext _dbContext = default!;
    private ITenantManager _tenantManager = default!;
    private ITenantConnectionConfigurationManager _manager = default!;
    private ITenantConnectionConfigurationStore _store = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddMultiTenancyEfCore<TestDbContext>();

        _provider = services.BuildServiceProvider();
        var provider = _provider;
        _dbContext = provider.GetRequiredService<TestDbContext>();
        await _dbContext.Database.EnsureCreatedAsync();
        _tenantManager = provider.GetRequiredService<ITenantManager>();
        _manager = provider.GetRequiredService<ITenantConnectionConfigurationManager>();
        _store = provider.GetRequiredService<ITenantConnectionConfigurationStore>();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Shared_database_rejects_secret_references()
    {
        var tenant = await CreateTenantAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.SharedDatabase,
            "runtime/tenant",
            null,
            expectedVersion: null));
    }

    [Theory]
    [InlineData(null, "migration/tenant")]
    [InlineData("runtime/tenant", null)]
    [InlineData("", "migration/tenant")]
    [InlineData("runtime/tenant", "  ")]
    public async Task Dedicated_database_requires_both_secret_references(
        string? runtimeSecretReference,
        string? migrationSecretReference)
    {
        var tenant = await CreateTenantAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            runtimeSecretReference,
            migrationSecretReference,
            expectedVersion: null));
    }

    /// <summary>
    /// 创建时间与修改时间由管理器填充，不依赖宿主是否接入审计层。
    /// </summary>
    /// <remarks>
    /// 这一行决定该租户的数据落在哪个库，所以"配置在何时被改过"必须无条件可查。
    /// 用户字段（<c>CreatorId</c> / <c>LastModifierId</c>）由宿主审计层补充：控制面上下文
    /// 用 <c>ChangeTracker.EnableCreationAuditing</c> 接上同一时机原语，本测试的上下文没接，
    /// 因此这里只钉时间字段。
    /// </remarks>
    [Fact]
    public async Task Set_fills_creation_and_modification_times()
    {
        var tenant = await CreateTenantAsync();

        await _manager.SetAsync(tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        var created = await _dbContext.Set<TenantConnectionRecord>()
            .AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id);
        Assert.NotEqual(default, created.CreationTime);
        Assert.Null(created.LastModificationTime);

        await _manager.SetAsync(tenant.Id, TenantDatabaseMode.DedicatedDatabase, "runtime/t", "migration/t", expectedVersion: 1);

        var updated = await _dbContext.Set<TenantConnectionRecord>()
            .AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id);
        Assert.Equal(created.CreationTime, updated.CreationTime);
        Assert.NotNull(updated.LastModificationTime);
    }

    [Fact]
    public async Task Set_creates_one_record_and_updates_increment_the_version()
    {
        var tenant = await CreateTenantAsync();

        var created = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/tenant-v1",
            "migration/tenant-v1",
            expectedVersion: null);
        var updated = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/tenant-v2",
            "migration/tenant-v2",
            expectedVersion: 1);

        Assert.Equal(1, created.Version);
        Assert.Equal(2, updated.Version);
        Assert.Single(await _dbContext.Set<TenantConnectionRecord>().ToListAsync());

        var stored = await _store.FindAsync(tenant.Id);
        Assert.NotNull(stored);
        Assert.Equal("runtime/tenant-v2", stored.RuntimeSecretReference);
        Assert.Equal("migration/tenant-v2", stored.MigrationSecretReference);
    }

    [Fact]
    public async Task Missing_tenant_is_rejected()
    {
        await Assert.ThrowsAsync<TenantNotFoundException>(() => _manager.SetAsync(
            Guid.NewGuid(),
            TenantDatabaseMode.SharedDatabase,
            null,
            null,
            expectedVersion: null));
    }

    [Fact]
    public async Task Shared_database_is_an_explicit_record_not_a_missing_configuration()
    {
        var tenant = await CreateTenantAsync();

        Assert.Null(await _store.FindAsync(tenant.Id));

        var configuration = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.SharedDatabase,
            null,
            null,
            expectedVersion: null);

        Assert.Equal(TenantDatabaseMode.SharedDatabase, configuration.DatabaseMode);
        Assert.Null(configuration.RuntimeSecretReference);
        Assert.Null(configuration.MigrationSecretReference);
    }

    [Fact]
    public async Task Configuration_string_representation_does_not_expose_secret_references()
    {
        var tenant = await CreateTenantAsync();
        var configuration = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/highly-sensitive-reference",
            "migration/highly-sensitive-reference",
            expectedVersion: null);

        var text = configuration.ToString();

        Assert.DoesNotContain("highly-sensitive", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 预期版本与实际状态的四种组合，只有两种放行
    /// </summary>
    /// <remarks>
    /// 丢更新在这一行是静默的：两个管理员同时改放置模式，后提交的那次覆盖前一次且不报错，
    /// 而"该租户的数据落在哪个库"已经变了。因此四种组合都要钉住，不能只测匹配那一种。
    /// </remarks>
    [Fact]
    public async Task Expected_version_must_match_actual_state()
    {
        var tenant = await CreateTenantAsync();

        // 预期不存在 + 实际不存在 → 放行（创建）
        var created = await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);
        Assert.Equal(1, created.Version);

        // 预期不存在 + 实际已存在 → 冲突。以为在创建，实际会覆盖别人刚建的配置
        var createOverExisting = await Assert.ThrowsAsync<TenantConnectionVersionConflictException>(
            () => _manager.SetAsync(
                tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null));
        Assert.Null(createOverExisting.ExpectedVersion);
        Assert.Equal(1, createOverExisting.ActualVersion);

        // 预期陈旧版本 + 实际更新 → 冲突
        var stale = await Assert.ThrowsAsync<TenantConnectionVersionConflictException>(
            () => _manager.SetAsync(
                tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: 99));
        Assert.Equal(99, stale.ExpectedVersion);
        Assert.Equal(1, stale.ActualVersion);

        // 预期版本 + 实际相符 → 放行（更新）
        var updated = await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: 1);
        Assert.Equal(2, updated.Version);
    }

    /// <summary>冲突时不得留下任何写入</summary>
    [Fact]
    public async Task Version_conflict_leaves_the_stored_configuration_untouched()
    {
        var tenant = await CreateTenantAsync();
        await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        await Assert.ThrowsAsync<TenantConnectionVersionConflictException>(
            () => _manager.SetAsync(
                tenant.Id,
                TenantDatabaseMode.DedicatedDatabase,
                "runtime/hijack",
                "migration/hijack",
                expectedVersion: 42));

        var stored = await _store.FindAsync(tenant.Id);
        Assert.NotNull(stored);
        Assert.Equal(TenantDatabaseMode.SharedDatabase, stored.DatabaseMode);
        Assert.Equal(1, stored.Version);
        Assert.Null(stored.RuntimeSecretReference);
    }

    /// <summary>
    /// 在用租户不允许改连接配置
    /// </summary>
    /// <remarks>
    /// 路由改了不等于数据跟着走：资源服务按租户缓存解析结果，已缓存的实例继续写旧库、
    /// 冷启动的实例开始写新库，同一租户在两个物理位置同时产生新数据，且双方都不报错。
    /// 这是数据分叉，不是传播延迟，所以必须在唯一写入口上挡住，而不是靠流程约束。
    /// </remarks>
    [Fact]
    public async Task Changing_the_configuration_of_an_active_tenant_is_rejected()
    {
        var tenant = await CreateTenantAsync();
        await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        await _tenantManager.SetActiveAsync(tenant.Id, true);

        var rejected = await Assert.ThrowsAsync<TenantConnectionChangeRequiresInactiveTenantException>(
            () => _manager.SetAsync(
                tenant.Id,
                TenantDatabaseMode.DedicatedDatabase,
                "runtime/moved",
                "migration/moved",
                expectedVersion: 1));
        Assert.Equal(tenant.Id, rejected.TenantId);

        // 被拒之后配置必须原样
        var stored = await _store.FindAsync(tenant.Id);
        Assert.NotNull(stored);
        Assert.Equal(TenantDatabaseMode.SharedDatabase, stored.DatabaseMode);
        Assert.Equal(1, stored.Version);
    }

    /// <summary>停用之后可以改——这就是标准切换流程的第 4 步</summary>
    [Fact]
    public async Task Changing_the_configuration_is_allowed_once_the_tenant_is_deactivated()
    {
        var tenant = await CreateTenantAsync();
        await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);
        await _tenantManager.SetActiveAsync(tenant.Id, true);
        await _tenantManager.SetActiveAsync(tenant.Id, false);

        var moved = await _manager.SetAsync(
            tenant.Id,
            TenantDatabaseMode.DedicatedDatabase,
            "runtime/moved",
            "migration/moved",
            expectedVersion: 1);

        Assert.Equal(TenantDatabaseMode.DedicatedDatabase, moved.DatabaseMode);
        Assert.Equal(2, moved.Version);
    }

    /// <summary>
    /// 首次创建不受停用态约束
    /// </summary>
    /// <remarks>
    /// 此前没有任何配置，解析器对该租户直接抛 404 且从不写缓存，不存在需要排空的陈旧路由。
    /// 把首创也一并禁掉会让"给漏配的在用租户补配置"变成必须先停机，而那本来是修复动作。
    /// </remarks>
    [Fact]
    public async Task First_configuration_is_allowed_even_while_the_tenant_is_active()
    {
        var tenant = await CreateTenantAsync();
        await _tenantManager.SetActiveAsync(tenant.Id, true);

        var created = await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        Assert.Equal(1, created.Version);
    }

    /// <summary>
    /// 真正的同时提交也必须是 409，不能是 500
    /// </summary>
    /// <remarks>
    /// <para><c>expectedVersion</c> 预检只挡得住"读到的版本已过期"这种串行陈旧。真正的竞态是
    /// 两个事务都先读到 v1：双方预检都过，先提交的把版本推到 v2，后提交的并发令牌命中 0 行。
    /// 数据库因此没有丢更新，但若不转换异常，调用方拿到的是 500——而契约承诺 409。</para>
    /// <para>用两个独立 DbContext 各自先把记录读进变更跟踪器来构造这个交错：manager 内部再查时
    /// 拿到的是本上下文已跟踪的实例（<c>OriginalValue</c> 仍是 v1），
    /// 于是两边的预检都通过。串行调用同一个 manager 是复现不出来的。</para>
    /// </remarks>
    [Fact]
    public async Task Simultaneous_commits_surface_as_a_version_conflict_not_a_server_error()
    {
        var tenant = await CreateTenantAsync();
        await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        await using var scopeA = _provider.CreateAsyncScope();
        await using var scopeB = _provider.CreateAsyncScope();

        // 屏障：两个上下文都必须在任何一方保存之前读到 v1
        await TrackAsync(scopeA, tenant.Id);
        await TrackAsync(scopeB, tenant.Id);

        var managerA = scopeA.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();
        var managerB = scopeB.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();

        var winner = await managerA.SetAsync(
            tenant.Id, TenantDatabaseMode.DedicatedDatabase, "runtime/a", "migration/a", expectedVersion: 1);
        Assert.Equal(2, winner.Version);

        var conflict = await Assert.ThrowsAsync<TenantConnectionVersionConflictException>(
            () => managerB.SetAsync(
                tenant.Id, TenantDatabaseMode.DedicatedDatabase, "runtime/b", "migration/b", expectedVersion: 1));

        // 报出的"实际版本"必须是重读后的真实值，好让调用方知道该以什么为基准重试
        Assert.Equal(1, conflict.ExpectedVersion);
        Assert.Equal(2, conflict.ActualVersion);
        Assert.IsType<DbUpdateConcurrencyException>(conflict.InnerException);

        // 败者的写入不得落库
        var stored = await _store.FindAsync(tenant.Id);
        Assert.NotNull(stored);
        Assert.Equal("runtime/a", stored.RuntimeSecretReference);

        static async Task TrackAsync(AsyncServiceScope scope, Guid tenantId)
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Set<TenantConnectionRecord>()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId);
        }
    }

    /// <summary>
    /// 路由事务读到"已停用"之后、写入之前，激活提交 —— 必须由数据库拦下
    /// </summary>
    /// <remarks>
    /// <para>这是「改路由要求租户已停用」这条不变量真正需要证明的交错。只读一次
    /// <c>IsActive</c> 在 READ COMMITTED 下挡不住它：读不持锁，激活可以在读之后提交。
    /// 若不拦，最终租户是启用状态、热实例用旧路由、冷实例用新路由，
    /// 同一租户同时往两个库写新数据，双方都不报错。</para>
    /// <para>用两个独立作用域构造交错：路由侧先把租户行读进变更跟踪器（此时 IsActive=false），
    /// 激活侧在另一个上下文把它改成启用并提交，然后路由侧才写入。
    /// 路由侧带的是过期的租户版本，UPDATE 命中 0 行。</para>
    /// </remarks>
    [Fact]
    public async Task Activation_committed_between_the_route_read_and_write_is_rejected()
    {
        var tenant = await CreateTenantAsync();
        await _manager.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        await using var routeScope = _provider.CreateAsyncScope();
        var routeDbContext = routeScope.ServiceProvider.GetRequiredService<TestDbContext>();

        // 路由侧先读租户行：此刻 IsActive = false，检查会通过
        var trackedTenant = await routeDbContext.Set<TenantRecord>()
            .FirstAsync(x => x.Id == tenant.Id);
        Assert.False(trackedTenant.IsActive);
        await routeDbContext.Set<TenantConnectionRecord>().FirstOrDefaultAsync(x => x.TenantId == tenant.Id);

        // 另一个上下文完成激活并提交
        await using (var activationScope = _provider.CreateAsyncScope())
        {
            var activationManager = activationScope.ServiceProvider.GetRequiredService<ITenantManager>();
            await activationManager.SetActiveAsync(tenant.Id, true);
        }

        // 路由侧此时才写入：它读到的租户版本已过期
        var routeManager = routeScope.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();
        await Assert.ThrowsAsync<TenantConcurrencyConflictException>(
            () => routeManager.SetAsync(
                tenant.Id,
                TenantDatabaseMode.DedicatedDatabase,
                "runtime/moved",
                "migration/moved",
                expectedVersion: 1));

        // 路由必须原样：租户已被激活，而它的数据落点没有被改动
        var stored = await _store.FindAsync(tenant.Id);
        Assert.NotNull(stored);
        Assert.Equal(TenantDatabaseMode.SharedDatabase, stored.DatabaseMode);
    }

    /// <summary>
    /// 并发首次创建：败者是 409，不是主键冲突导致的 500
    /// </summary>
    /// <remarks>
    /// 两个请求都以 <c>expectedVersion: null</c> 创建，都读到"配置不存在"，
    /// 都要推进同一个租户版本 —— 败者在插入之前就冲突。
    /// 靠租户版本而不是识别主键冲突：后者是 provider 相关的，
    /// 且容易把别的约束错误也误判成 409。
    /// </remarks>
    [Fact]
    public async Task Concurrent_first_creation_conflicts_instead_of_violating_the_primary_key()
    {
        var tenant = await CreateTenantAsync();

        await using var scopeA = _provider.CreateAsyncScope();
        await using var scopeB = _provider.CreateAsyncScope();

        // 屏障：两边都要在任何一方写入之前读到租户行与"配置不存在"
        foreach (var scope in new[] { scopeA, scopeB })
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Set<TenantRecord>().FirstAsync(x => x.Id == tenant.Id);
            Assert.Null(await dbContext.Set<TenantConnectionRecord>()
                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id));
        }

        var managerA = scopeA.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();
        var managerB = scopeB.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();

        var created = await managerA.SetAsync(
            tenant.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);
        Assert.Equal(1, created.Version);

        // 败者在自己的 SetAsync 里重新查询时会看到 A 已提交的行，于是被 expectedVersion
        // 预检拦下——报的是"预期不存在却已存在"，比"租户被并发修改"更准确。
        // 两者都是 409；关键是不会落成主键冲突导致的 500
        var conflict = await Assert.ThrowsAsync<TenantConnectionVersionConflictException>(
            () => managerB.SetAsync(
                tenant.Id, TenantDatabaseMode.DedicatedDatabase, "runtime/b", "migration/b",
                expectedVersion: null));
        Assert.Null(conflict.ExpectedVersion);
        Assert.Equal(1, conflict.ActualVersion);

        Assert.Single(await _dbContext.Set<TenantConnectionRecord>().AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// 陈旧改名必须是 409 并发，而不是泄漏成 500
    /// </summary>
    /// <remarks>
    /// <para>改名同时参与两个竞争：名称唯一，和租户生命周期版本。
    /// <c>DbUpdateConcurrencyException</c> 继承 <c>DbUpdateException</c>，
    /// 所以若把名称冲突那一档写在前面，陈旧改名会先走进名称判定；
    /// 而库里并没有别的同名租户，于是原样上抛 EF 异常——调用方拿到 500，
    /// 而项目里明明已经定义了对应的 409。</para>
    /// <para>本用例构造的正是这个交错：改名侧先读到租户行，另一侧完成启停并提交，
    /// 改名侧才写入。此时它带的租户版本已过期，且没有任何名称冲突。</para>
    /// </remarks>
    [Fact]
    public async Task A_stale_rename_conflicts_as_concurrency_not_as_a_server_error()
    {
        var tenant = await CreateTenantAsync();

        await using var renameScope = _provider.CreateAsyncScope();
        var renameDbContext = renameScope.ServiceProvider.GetRequiredService<TestDbContext>();

        // 改名侧先把租户行读进跟踪器
        await renameDbContext.Set<TenantRecord>().FirstAsync(x => x.Id == tenant.Id);

        // 另一侧完成启停并提交，推进版本
        await using (var activationScope = _provider.CreateAsyncScope())
        {
            var other = activationScope.ServiceProvider.GetRequiredService<ITenantManager>();
            await other.SetActiveAsync(tenant.Id, true);
        }

        // 改名侧此时才写入：版本已过期，且新名字没有被任何人占用
        var renameManager = renameScope.ServiceProvider.GetRequiredService<ITenantManager>();
        await Assert.ThrowsAsync<TenantConcurrencyConflictException>(
            () => renameManager.UpdateAsync(tenant.Id, $"renamed-{Guid.NewGuid():N}", null));
    }

    /// <summary>名称确实被他人占用时仍报名称重复，不被并发那一档吞掉</summary>
    [Fact]
    public async Task A_genuine_name_collision_still_reports_a_duplicate_name()
    {
        var taken = await CreateTenantAsync();
        var subject = await CreateTenantAsync();

        await Assert.ThrowsAsync<DuplicateTenantNameException>(
            () => _tenantManager.UpdateAsync(subject.Id, taken.Name, null));
    }

    /// <summary>
    /// 并发冲突只丢弃本次操作涉及的行，不碰同一工作单元里的其它实体
    /// </summary>
    /// <remarks>
    /// <c>DbContext</c> 是宿主的工作单元。用 <c>ChangeTracker.Clear()</c> 收拾现场会把
    /// 调用方尚未提交的其它实体一并抹掉——调用方捕获 409 后继续处理时，
    /// 那些修改静默消失且没有任何信号。冲突处理只该管自己那两行。
    /// </remarks>
    [Fact]
    public async Task A_conflict_does_not_discard_unrelated_pending_changes()
    {
        var target = await CreateTenantAsync();
        var unrelated = await CreateTenantAsync();
        await _manager.SetAsync(
            target.Id, TenantDatabaseMode.SharedDatabase, null, null, expectedVersion: null);

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // 同一工作单元里，调用方对另一个租户做了尚未提交的修改
        var pending = await dbContext.Set<TenantRecord>().FirstAsync(x => x.Id == unrelated.Id);
        pending.DisplayName = "edited-by-caller";

        // 目标租户上制造并发冲突：另一个上下文先推进它的版本
        await dbContext.Set<TenantRecord>().FirstAsync(x => x.Id == target.Id);
        await dbContext.Set<TenantConnectionRecord>().FirstOrDefaultAsync(x => x.TenantId == target.Id);
        await using (var other = _provider.CreateAsyncScope())
        {
            await other.ServiceProvider.GetRequiredService<ITenantManager>()
                .SetActiveAsync(target.Id, true);
        }

        var manager = scope.ServiceProvider.GetRequiredService<ITenantConnectionConfigurationManager>();
        await Assert.ThrowsAsync<TenantConcurrencyConflictException>(
            () => manager.SetAsync(
                target.Id, TenantDatabaseMode.DedicatedDatabase, "runtime/x", "migration/x",
                expectedVersion: 1));

        // 调用方的那笔修改必须还在，并且仍能提交
        Assert.Equal(EntityState.Modified, dbContext.Entry(pending).State);
        await dbContext.SaveChangesAsync();

        var reread = await _dbContext.Set<TenantRecord>().AsNoTracking()
            .FirstAsync(x => x.Id == unrelated.Id);
        Assert.Equal("edited-by-caller", reread.DisplayName);
    }

    private Task<TenantConfiguration> CreateTenantAsync() =>
        _tenantManager.CreateAsync($"tenant-{Guid.NewGuid():N}", null, isActive: false);

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
