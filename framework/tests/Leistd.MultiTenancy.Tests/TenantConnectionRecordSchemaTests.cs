using Leistd.MultiTenancy.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;

namespace Leistd.MultiTenancy.Tests;

/// <summary>
/// 租户连接记录的表级不变量：模式与 Secret 引用的一致性由数据库检查约束钉住，
/// 不只靠管理器校验。
/// </summary>
/// <remarks>
/// 这一行决定该租户的数据落在哪个库。绕过管理器直接写库（迁移脚本、运维手工修数、
/// 未来新增的写入路径）同样不能造出"独立库但没有 Secret 引用"——那样的行解析不出连接，
/// 该租户的请求会全部失败；也不能造出"共享库却带着 Secret 引用"——那是配置意图不明。
/// </remarks>
public class TenantConnectionRecordSchemaTests : IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private SchemaDbContext _dbContext = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SchemaDbContext>().UseSqlite(_connection).Options;
        _dbContext = new SchemaDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    // 独立库却缺 Secret 引用：解析不出连接
    [InlineData(TenantDatabaseMode.DedicatedDatabase, null, null)]
    [InlineData(TenantDatabaseMode.DedicatedDatabase, "runtime", null)]
    [InlineData(TenantDatabaseMode.DedicatedDatabase, null, "migration")]
    // 共享库却带 Secret 引用：配置意图不明
    [InlineData(TenantDatabaseMode.SharedDatabase, "runtime", "migration")]
    [InlineData(TenantDatabaseMode.SharedDatabase, "runtime", null)]
    public async Task Inconsistent_mode_and_secret_references_are_rejected_by_the_database(
        TenantDatabaseMode mode,
        string? runtimeSecret,
        string? migrationSecret)
    {
        _dbContext.Set<TenantConnectionRecord>().Add(new TenantConnectionRecord
        {
            TenantId = await SeedTenantAsync(),
            DatabaseMode = mode,
            RuntimeSecretReference = runtimeSecret,
            MigrationSecretReference = migrationSecret,
            Version = 1
        });

        var error = await Record.ExceptionAsync(() => _dbContext.SaveChangesAsync());
        Assert.NotNull(error);
        _dbContext.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(TenantDatabaseMode.SharedDatabase, null, null)]
    [InlineData(TenantDatabaseMode.DedicatedDatabase, "runtime", "migration")]
    public async Task Consistent_mode_and_secret_references_are_accepted(
        TenantDatabaseMode mode,
        string? runtimeSecret,
        string? migrationSecret)
    {
        _dbContext.Set<TenantConnectionRecord>().Add(new TenantConnectionRecord
        {
            TenantId = await SeedTenantAsync(),
            DatabaseMode = mode,
            RuntimeSecretReference = runtimeSecret,
            MigrationSecretReference = migrationSecret,
            Version = 1
        });

        await _dbContext.SaveChangesAsync();
        Assert.Equal(1, await _dbContext.Set<TenantConnectionRecord>().CountAsync());
    }

    private async Task<Guid> SeedTenantAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tenant = new TenantRecord { Name = $"t{suffix}", NormalizedName = $"T{suffix}".ToUpperInvariant() };
        _dbContext.Set<TenantRecord>().Add(tenant);
        await _dbContext.SaveChangesAsync();
        return tenant.Id;
    }

    private sealed class SchemaDbContext(DbContextOptions<SchemaDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureMultiTenancy();
    }
}
