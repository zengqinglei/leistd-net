using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.Timing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

/// <summary>
/// 租户存储与管理器的关系型行为验证（Sqlite，见 csproj 内注释）。
/// </summary>
public class TenantStoreManagerTests : IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private TestTenantDbContext _db = default!;
    private IDistributedCache _cache = default!;
    private EfCoreTenantStore<TestTenantDbContext> _store = default!;
    private EfCoreTenantManager<TestTenantDbContext> _manager = default!;

    internal class TestTenantDbContext(DbContextOptions<TestTenantDbContext> options) : DbContext(options)
    {
        /// <summary>测试用的显示名长度上限，用来制造与名称无关的约束失败。</summary>
        internal const int DisplayNameCheckLimit = 32;

        /// <summary>与租户无关的业务实体：验证管理器失败时不连带丢弃调用方的待提交变更。</summary>
        public DbSet<TestNote> Notes => Set<TestNote>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigureMultiTenancy();

            // SQLite 不强制 VARCHAR 长度，需显式 CHECK 才能触发非名称冲突的写入失败
            modelBuilder.Entity<TestNote>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Text).HasMaxLength(64);
            });

            modelBuilder.Entity<TenantRecord>()
                .ToTable(t => t.HasCheckConstraint(
                    "CK_Test_TenantRecord_DisplayName",
                    $"\"{nameof(TenantRecord.DisplayName)}\" IS NULL OR length(\"{nameof(TenantRecord.DisplayName)}\") <= {DisplayNameCheckLimit}"));
        }
    }

    /// <summary>宿主工作单元里的普通业务实体。</summary>
    internal class TestNote
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        public string Text { get; set; } = string.Empty;
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestTenantDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        _store = new EfCoreTenantStore<TestTenantDbContext>(_db, _cache);
        _manager = new EfCoreTenantManager<TestTenantDbContext>(
            _db, new UpperInvariantTenantNormalizer(), _cache, new UtcClockProvider());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Create_normalizes_name_and_store_finds_by_id_and_name()
    {
        var record = await _manager.CreateAsync("Acme", "Acme Inc.", isActive: true);

        Assert.Equal("ACME", record.NormalizedName);

        var byId = await _store.FindAsync(record.Id);
        Assert.NotNull(byId);
        Assert.Equal("Acme", byId.Name);
        Assert.True(byId.IsActive);

        var byName = await _store.FindByNameAsync("ACME");
        Assert.NotNull(byName);
        Assert.Equal(record.Id, byName.Id);
    }

    /// <summary>
    /// 停用态创建：调用方要在创建后继续初始化租户数据时，租户不能一出生就对外可用。
    /// </summary>
    [Fact]
    public async Task Tenant_can_be_created_inactive_and_activated_afterwards()
    {
        var record = await _manager.CreateAsync("Acme", null, isActive: false);

        Assert.False(record.IsActive);
        Assert.False((await _store.FindAsync(record.Id))!.IsActive);
        Assert.False((await _store.FindByNameAsync("ACME"))!.IsActive);

        await _manager.SetActiveAsync(record.Id, true);
        Assert.True((await _store.FindAsync(record.Id))!.IsActive);
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_case_insensitively()
    {
        await _manager.CreateAsync("Acme", null, isActive: true);

        await Assert.ThrowsAsync<DuplicateTenantNameException>(() => _manager.CreateAsync("ACME", null, isActive: true));
        await Assert.ThrowsAsync<DuplicateTenantNameException>(() => _manager.CreateAsync("acme", null, isActive: true));
    }

    [Fact]
    public async Task Rename_invalidates_old_and_new_name_cache()
    {
        var record = await _manager.CreateAsync("Acme", null, isActive: true);

        // 预热两个缓存键
        Assert.NotNull(await _store.FindByNameAsync("ACME"));
        Assert.NotNull(await _store.FindAsync(record.Id));

        await _manager.UpdateAsync(record.Id, "Contoso", null);

        Assert.Null(await _store.FindByNameAsync("ACME"));
        var byNewName = await _store.FindByNameAsync("CONTOSO");
        Assert.NotNull(byNewName);
        Assert.Equal("Contoso", byNewName.Name);

        var byId = await _store.FindAsync(record.Id);
        Assert.NotNull(byId);
        Assert.Equal("Contoso", byId.Name);
    }

    [Fact]
    public async Task Deactivation_is_visible_after_cache_invalidation()
    {
        var record = await _manager.CreateAsync("Acme", null, isActive: true);
        Assert.True((await _store.FindAsync(record.Id))!.IsActive);

        await _manager.SetActiveAsync(record.Id, false);

        // 管理器写入即失效缓存：在途会话的下一次校验立刻看到停用
        Assert.False((await _store.FindAsync(record.Id))!.IsActive);
    }

    [Fact]
    public async Task Store_reads_through_cache_until_invalidated()
    {
        var record = await _manager.CreateAsync("Acme", null, isActive: true);
        Assert.NotNull(await _store.FindAsync(record.Id));

        // 绕过管理器直接改库：缓存仍返回旧值——这正是"写入必须经 ITenantManager"约定的原因
        await _db.Set<TenantRecord>()
            .Where(t => t.Id == record.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false));

        Assert.True((await _store.FindAsync(record.Id))!.IsActive);

        // 经管理器的任意写入触发失效后读到真实状态
        await _manager.SetActiveAsync(record.Id, false);
        Assert.False((await _store.FindAsync(record.Id))!.IsActive);
    }

    [Fact]
    public async Task Delete_is_soft_and_hides_tenant_from_store()
    {
        var record = await _manager.CreateAsync("Acme", null, isActive: true);
        Assert.NotNull(await _store.FindAsync(record.Id));

        await _manager.DeleteAsync(record.Id);

        Assert.Null(await _store.FindAsync(record.Id));
        Assert.Null(await _store.FindByNameAsync("ACME"));

        // 软删除：行仍在库中，业务数据可追溯
        var raw = await _db.Set<TenantRecord>().IgnoreQueryFilters()
            .SingleAsync(t => t.Id == record.Id);
        Assert.True(raw.IsDeleted);
        Assert.NotNull(raw.DeletionTime);
    }

    [Fact]
    public async Task Same_name_can_be_recreated_after_delete()
    {
        var first = await _manager.CreateAsync("Acme", null, isActive: true);
        await _manager.DeleteAsync(first.Id);

        var second = await _manager.CreateAsync("Acme", null, isActive: true);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, (await _store.FindByNameAsync("ACME"))!.Id);
    }

    [Fact]
    public async Task Update_missing_tenant_throws_not_found()
    {
        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => _manager.UpdateAsync(Guid.NewGuid(), "x", null));
    }

    [Fact]
    public async Task Database_rejects_duplicate_active_name_even_bypassing_the_manager()
    {
        await _manager.CreateAsync("Acme", null, isActive: true);

        // 绕过管理器预检直接写库：唯一性由数据库的部分唯一索引兜住，
        // 这正是并发创建（两个请求同时通过预检）走到的路径
        _db.Set<TenantRecord>().Add(new TenantRecord { Name = "Acme", NormalizedName = "ACME" });

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// 真实竞争：预检通过之后、SaveChanges 落库之前，另一方抢先写入同名租户。
    /// </summary>
    /// <remarks>
    /// 用 SaveChanges 拦截器精确插入竞争写入——这是唯一能越过管理器预检的时点。
    /// 若竞争者提前提交，预检就会直接拒绝，唯一索引那条路径一次都走不到（曾经的测试就是这样空转的）。
    /// </remarks>
    [Fact]
    public async Task Losing_a_race_after_the_precheck_surfaces_as_duplicate_name()
    {
        var competitor = new RaceInjectingInterceptor(_connection, "Contoso", "CONTOSO");

        var racedOptions = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(competitor)
            .Options;

        await using var racedDb = new TestTenantDbContext(racedOptions);
        var racedManager = new EfCoreTenantManager<TestTenantDbContext>(
            racedDb, new UpperInvariantTenantNormalizer(), _cache, new UtcClockProvider());

        // 预检时库中无同名租户 → 通过；拦截器在 flush 前写入同名行 → 唯一索引拒绝本次插入
        await Assert.ThrowsAsync<DuplicateTenantNameException>(() => racedManager.CreateAsync("Contoso", null, isActive: true));
        Assert.True(competitor.Injected, "竞争写入未发生，本测试没有覆盖数据库冲突路径");
    }

    [Fact]
    public async Task Other_constraint_failures_are_not_reported_as_duplicate_name()
    {
        // 名称未变、因别的约束失败：不能被当成名称重复。
        // 早期实现只查"同名是否存在"，更新时必然命中自己，任何写入错误都会被误报成 409
        var record = await _manager.CreateAsync("Acme", null, isActive: true);

        var tooLongDisplayName = new string('x', TestTenantDbContext.DisplayNameCheckLimit + 1);
        var ex = await Record.ExceptionAsync(() => _manager.UpdateAsync(record.Id, "Acme", tooLongDisplayName));

        Assert.NotNull(ex);
        Assert.IsNotType<DuplicateTenantNameException>(ex);

        // 失败的修改必须从跟踪器里回滚：留着它，下一次保存就会把这个已知失败的值写出去
        var tracked = await _db.Set<TenantRecord>().FirstAsync(t => t.Id == record.Id);
        Assert.Equal(EntityState.Unchanged, _db.Entry(tracked).State);
        Assert.Null(tracked.DisplayName);

        await _db.SaveChangesAsync();
        Assert.Null((await _manager.FindAsync(record.Id))!.DisplayName);
    }

    /// <summary>
    /// 管理器的 DbContext 可能就是宿主的工作单元：名称冲突不能连带丢掉调用方尚未提交的业务变更。
    /// </summary>
    /// <remarks>
    /// <para>早期实现在冲突分支里 <c>ChangeTracker.Clear()</c>，会静默清空整个跟踪器——
    /// 调用方先改了业务实体、再调用租户管理器并捕获 409 继续执行时，那些修改凭空消失。</para>
    /// <para>必须走**竞争**路径而不是预检路径：预检拒绝时 SaveChanges 根本没被调用，
    /// 跟踪器也就没人动过，用例即使在有 <c>Clear()</c> 的实现下也是绿的（曾经就是这样空转的）。
    /// 只有预检通过、保存被唯一索引拒绝时，才会执行到丢弃逻辑。</para>
    /// </remarks>
    [Fact]
    public async Task Name_conflict_does_not_discard_unrelated_pending_changes()
    {
        var competitor = new RaceInjectingInterceptor(_connection, "Contoso", "CONTOSO");

        var racedOptions = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(competitor)
            .Options;

        await using var racedDb = new TestTenantDbContext(racedOptions);
        var racedManager = new EfCoreTenantManager<TestTenantDbContext>(
            racedDb, new UpperInvariantTenantNormalizer(), _cache, new UtcClockProvider());

        // 调用方在同一 DbContext（= 宿主工作单元）里改了业务实体，尚未提交
        var note = new TestNote { Text = "caller's pending work" };
        racedDb.Add(note);

        await Assert.ThrowsAsync<DuplicateTenantNameException>(() => racedManager.CreateAsync("Contoso", null, isActive: true));
        Assert.True(competitor.Injected, "竞争写入未发生，本测试没有覆盖数据库冲突路径");

        // 无关实体仍在跟踪器里等待提交，且能正常落库
        Assert.Equal(EntityState.Added, racedDb.Entry(note).State);
        await racedDb.SaveChangesAsync();
        Assert.Equal("caller's pending work", (await racedDb.Set<TestNote>().SingleAsync()).Text);
    }

    /// <summary>
    /// 预检拒绝时实体完全未被触碰——赋值必须发生在预检之后。
    /// </summary>
    /// <remarks>
    /// 若把赋值提到预检之前，被拒绝的改名会留在跟踪器里，下一次保存就把它写出去了。
    /// 保存失败路径的回滚由 <c>Other_constraint_failures_are_not_reported_as_duplicate_name</c> 覆盖。
    /// </remarks>
    [Fact]
    public async Task Precheck_rejection_leaves_the_record_untouched()
    {
        var target = await _manager.CreateAsync("Acme", null, isActive: true);
        await _manager.CreateAsync("Contoso", null, isActive: true);

        // 改成已被占用的名字：预检就会拒绝，实体上的赋值必须回滚
        await Assert.ThrowsAsync<DuplicateTenantNameException>(
            () => _manager.UpdateAsync(target.Id, "Contoso", null));

        // 跟踪器里不应残留"Contoso"这个改名意图
        await _db.SaveChangesAsync();
        var reloaded = await _manager.FindAsync(target.Id);
        Assert.Equal("Acme", reloaded!.Name);
    }

    /// <summary>
    /// 库已提交但缓存失效失败：异常上抛（管理员据此重试），库中状态已生效。
    /// </summary>
    /// <remarks>
    /// cache-aside 的失效是尽力而为的。这里锁死失败语义：不能因为缓存删不掉就把
    /// 已提交的停用/删除回滚（做不到），也不能把它咽下去报成功——那会让管理员以为
    /// 租户已经停了。异常上抛 + 库为准，重试即自愈（写路径读库不读缓存）。
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cache_invalidation_failure_after_commit_surfaces_but_database_state_stands(bool deleteInsteadOfDeactivate)
    {
        var record = await _manager.CreateAsync("Acme", null, isActive: true);

        var brokenCache = new FailingRemoveCache(_cache);
        var manager = new EfCoreTenantManager<TestTenantDbContext>(
            _db, new UpperInvariantTenantNormalizer(), brokenCache, new UtcClockProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() => deleteInsteadOfDeactivate
            ? manager.DeleteAsync(record.Id)
            : manager.SetActiveAsync(record.Id, false));

        // 库已提交：写路径读库不读缓存，重试会看到真实状态并再次尝试失效
        var raw = await _db.Set<TenantRecord>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == record.Id);
        if (deleteInsteadOfDeactivate)
        {
            Assert.True(raw.IsDeleted);
        }
        else
        {
            Assert.False(raw.IsActive);
            Assert.False(raw.IsDeleted);
        }
    }

    /// <summary>
    /// 缓存条目必须是绝对过期：失效失败时的暴露窗口要有上界。
    /// </summary>
    /// <remarks>
    /// 滑动过期下，持续有流量的租户其陈旧"启用"条目会被每个请求续命而永不过期——
    /// 停用一个繁忙租户可能永远不生效。这个断言锁住策略本身，因为它不可能由行为测试
    /// 观察到（要观察得等真实时钟走过过期点）。
    /// </remarks>
    [Fact]
    public async Task Cached_tenant_entries_expire_absolutely_so_failed_invalidation_self_heals()
    {
        var recording = new OptionsRecordingCache(_cache);
        var store = new EfCoreTenantStore<TestTenantDbContext>(_db, recording);

        var record = await _manager.CreateAsync("Acme", null, isActive: true);
        Assert.NotNull(await store.FindAsync(record.Id));

        var options = Assert.Single(recording.CapturedOptions);
        Assert.Null(options.SlidingExpiration);
        Assert.Equal(EfCoreTenantStore<TestTenantDbContext>.CacheDuration, options.AbsoluteExpirationRelativeToNow);
    }

    /// <summary>失效（Remove）失败、其余照常的缓存：模拟 Redis 抖动。</summary>
    private sealed class FailingRemoveCache(IDistributedCache inner) : IDistributedCache
    {
        public byte[]? Get(string key) => inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => inner.SetAsync(key, value, options, token);

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => throw new InvalidOperationException("cache unavailable");

        public Task RemoveAsync(string key, CancellationToken token = default)
            => throw new InvalidOperationException("cache unavailable");
    }

    /// <summary>记录写入时使用的过期策略。</summary>
    private sealed class OptionsRecordingCache(IDistributedCache inner) : IDistributedCache
    {
        internal List<DistributedCacheEntryOptions> CapturedOptions { get; } = [];

        public byte[]? Get(string key) => inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            CapturedOptions.Add(options);
            inner.Set(key, value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            CapturedOptions.Add(options);
            return inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
    }

    [Fact]
    public async Task Name_is_reusable_after_deletion()
    {
        var first = await _manager.CreateAsync("Acme", null, isActive: true);
        await _manager.DeleteAsync(first.Id);

        // 部分唯一索引只约束未删除行
        var recreated = await _manager.CreateAsync("Acme", null, isActive: true);
        Assert.NotEqual(first.Id, recreated.Id);
    }

    /// <summary>
    /// 在被测 DbContext 执行 SaveChanges 之前，用另一个连接抢先写入同名租户。
    /// </summary>
    private sealed class RaceInjectingInterceptor(
        SqliteConnection connection,
        string name,
        string normalizedName) : SaveChangesInterceptor
    {
        internal bool Injected { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Injected)
            {
                Injected = true;

                var options = new DbContextOptionsBuilder<TestTenantDbContext>()
                    .UseSqlite(connection)
                    .Options;

                await using var competitor = new TestTenantDbContext(options);
                competitor.Set<TenantRecord>().Add(new TenantRecord { Name = name, NormalizedName = normalizedName });
                await competitor.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Paged_query_filters_keyword_and_excludes_deleted()
    {
        await _manager.CreateAsync("Acme", "Acme Inc.", isActive: true);
        await _manager.CreateAsync("Contoso", null, isActive: true);
        var deleted = await _manager.CreateAsync("Acme-Old", null, isActive: true);
        await _manager.DeleteAsync(deleted.Id);

        var all = await _manager.GetPagedAsync(null, 0, 10);
        Assert.Equal(2, all.TotalCount);

        // 关键字大小写不敏感（按归一化名称匹配），软删行不计入
        var filtered = await _manager.GetPagedAsync("acme", 0, 10);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("Acme", filtered.Items.Single().Name);

        var paged = await _manager.GetPagedAsync(null, 1, 1);
        Assert.Equal(2, paged.TotalCount);
        Assert.Single(paged.Items);
    }
}
