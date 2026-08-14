using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.Timing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigureMultiTenancy();
        }
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
        var record = await _manager.CreateAsync("Acme", "Acme Inc.");

        Assert.Equal("ACME", record.NormalizedName);

        var byId = await _store.FindAsync(record.Id);
        Assert.NotNull(byId);
        Assert.Equal("Acme", byId.Name);
        Assert.True(byId.IsActive);

        var byName = await _store.FindByNameAsync("ACME");
        Assert.NotNull(byName);
        Assert.Equal(record.Id, byName.Id);
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_case_insensitively()
    {
        await _manager.CreateAsync("Acme");

        await Assert.ThrowsAsync<DuplicateTenantNameException>(() => _manager.CreateAsync("ACME"));
        await Assert.ThrowsAsync<DuplicateTenantNameException>(() => _manager.CreateAsync("acme"));
    }

    [Fact]
    public async Task Rename_invalidates_old_and_new_name_cache()
    {
        var record = await _manager.CreateAsync("Acme");

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
        var record = await _manager.CreateAsync("Acme");
        Assert.True((await _store.FindAsync(record.Id))!.IsActive);

        await _manager.SetActiveAsync(record.Id, false);

        // 管理器写入即失效缓存：在途会话的下一次校验立刻看到停用
        Assert.False((await _store.FindAsync(record.Id))!.IsActive);
    }

    [Fact]
    public async Task Store_reads_through_cache_until_invalidated()
    {
        var record = await _manager.CreateAsync("Acme");
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
        var record = await _manager.CreateAsync("Acme");
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
        var first = await _manager.CreateAsync("Acme");
        await _manager.DeleteAsync(first.Id);

        var second = await _manager.CreateAsync("Acme");

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
        await _manager.CreateAsync("Acme");

        // 绕过管理器预检直接写库：唯一性由数据库的部分唯一索引兜住，
        // 这正是并发创建（两个请求同时通过预检）走到的路径
        _db.Set<TenantRecord>().Add(new TenantRecord { Name = "Acme", NormalizedName = "ACME" });

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Manager_translates_database_conflict_into_duplicate_name_exception()
    {
        var first = await _manager.CreateAsync("Acme");

        // 模拟竞争：管理器预检通过后、保存前，另一方已写入同名租户
        using var connection = new SqliteConnection(_connection.ConnectionString);
        await connection.OpenAsync();

        // 同库另一连接写入（Sqlite in-memory 共享同一连接串下的库）
        var competitorOptions = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using (var competitor = new TestTenantDbContext(competitorOptions))
        {
            competitor.Set<TenantRecord>().Add(new TenantRecord { Name = "Contoso", NormalizedName = "CONTOSO" });
            await competitor.SaveChangesAsync();
        }

        // 落败方得到与预检一致的业务异常（映射 409），而不是原始 DbUpdateException（500）
        await Assert.ThrowsAsync<DuplicateTenantNameException>(() => _manager.CreateAsync("Contoso"));

        // 删除后名称可复用：部分唯一索引只约束未删除行
        await _manager.DeleteAsync(first.Id);
        var recreated = await _manager.CreateAsync("Acme");
        Assert.NotEqual(first.Id, recreated.Id);
    }

    [Fact]
    public async Task Paged_query_filters_keyword_and_excludes_deleted()
    {
        await _manager.CreateAsync("Acme", "Acme Inc.");
        await _manager.CreateAsync("Contoso");
        var deleted = await _manager.CreateAsync("Acme-Old");
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
