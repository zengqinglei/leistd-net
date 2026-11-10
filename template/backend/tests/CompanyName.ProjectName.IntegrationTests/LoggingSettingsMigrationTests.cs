using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>日志设置换取值域的数据迁移：在真实 PostgreSQL 上从上一条迁移升级、降级再升级。</summary>
/// <remarks>只改宿主级行；同名的租户行、宿主用户偏好与取值域之外的值原样保留，旧键不存在时不补行。</remarks>
public sealed class LoggingSettingsMigrationTests : IDisposable
{
    private const string MinimumLevel = "Logging.MinimumLevel";
    private const string RequestLevel = "Logging.RequestLevel";
    private const string RequestLogging = "Logging.RequestLogging";
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly string TenantScope = $"{TenantId}:t";
    private const string HostUserScope = "host:u:migration-user";

    private readonly string connectionString = PostgreSqlTestDatabase.CreateEmptyDatabase();

    public void Dispose() => PostgreSqlTestDatabase.DropDatabase(connectionString);

    [Theory]
    [InlineData("Verbose", "Trace", "Verbose", "false", "Verbose", "Verbose")]
    [InlineData("Debug", "Debug", "Debug", "false", "Debug", "Verbose")]
    [InlineData("Information", "Information", "Information", "true", "Information", "Information")]
    [InlineData("Warning", "Warning", "Warning", "true", "Warning", "Information")]
    [InlineData("Error", "Error", "Error", "true", "Error", "Information")]
    [InlineData("Fatal", "Critical", "Fatal", "true", "Fatal", "Information")]
    public async Task Host_rows_are_mapped_up_and_down(
        string oldValue, string upLevel, string requestOld, string upRequest, string downLevel, string downRequest)
    {
        await using var context = PostgreSqlTestDatabase.CreateBusinessContext(connectionString);
        var (previous, target) = Migrations(context);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        await InsertAsync("host:t", null, null, MinimumLevel, oldValue);
        await InsertAsync("host:t", null, null, RequestLevel, requestOld);
        await InsertAsync(TenantScope, TenantId, null, RequestLevel, "Verbose");
        await InsertAsync(TenantScope, TenantId, null, MinimumLevel, "Fatal");
        await InsertAsync(HostUserScope, null, "migration-user", MinimumLevel, "Fatal");

        await migrator.MigrateAsync(target);
        Assert.Equal(upLevel, await ValueAsync("host:t", MinimumLevel));
        Assert.Equal(upRequest, await ValueAsync("host:t", RequestLogging));
        Assert.Null(await ValueAsync("host:t", RequestLevel));
        await AssertUntouchedAsync();

        await migrator.MigrateAsync(previous);
        Assert.Equal(downLevel, await ValueAsync("host:t", MinimumLevel));
        Assert.Equal(downRequest, await ValueAsync("host:t", RequestLevel));
        Assert.Null(await ValueAsync("host:t", RequestLogging));
        await AssertUntouchedAsync();

        // 降级后再升级：按降级后的值重新映射
        await migrator.MigrateAsync(target);
        Assert.Equal(upLevel, await ValueAsync("host:t", MinimumLevel));
        Assert.Equal(downRequest == "Verbose" ? "false" : "true", await ValueAsync("host:t", RequestLogging));
    }

    [Fact]
    public async Task Missing_or_unknown_values_are_left_alone()
    {
        await using var context = PostgreSqlTestDatabase.CreateBusinessContext(connectionString);
        var (previous, target) = Migrations(context);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        await InsertAsync("host:t", null, null, MinimumLevel, "Chatty");

        await migrator.MigrateAsync(target);

        Assert.Equal("Chatty", await ValueAsync("host:t", MinimumLevel));
        Assert.Null(await ValueAsync("host:t", RequestLogging));
        Assert.Null(await ValueAsync("host:t", RequestLevel));
    }

    // 目标迁移与它的上一条：取迁移集里的实际顺序，不写死前一条的名字（随生成形态不同）
    private static (string Previous, string Target) Migrations(MyProjectDbContext context)
    {
        var all = context.Database.GetMigrations().ToList();
        var index = all.FindIndex(id => id.EndsWith("_MigrateLoggingSettings", StringComparison.Ordinal));
        Assert.True(index > 0, "MigrateLoggingSettings must follow an earlier migration");
        return (all[index - 1], all[index]);
    }

    private async Task AssertUntouchedAsync()
    {
        Assert.Equal("Verbose", await ValueAsync(TenantScope, RequestLevel));
        Assert.Equal("Fatal", await ValueAsync(TenantScope, MinimumLevel));
        Assert.Null(await ValueAsync(TenantScope, RequestLogging));
        Assert.Equal("Fatal", await ValueAsync(HostUserScope, MinimumLevel));
    }

    private async Task InsertAsync(string scopeKey, Guid? tenantId, string? userId, string name, string value)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""INSERT INTO "{DatabaseSchema.Name}"."SettingRecord" ("Id", "Name", "ScopeKey", "TenantId", "UserId", "Value") VALUES (@id, @name, @scope, @tenant, @user, @value)""",
            connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("scope", scopeKey);
        command.Parameters.AddWithValue("tenant", (object?)tenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("value", value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ValueAsync(string scopeKey, string name)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""SELECT "Value" FROM "{DatabaseSchema.Name}"."SettingRecord" WHERE "ScopeKey" = @scope AND "Name" = @name""",
            connection);
        command.Parameters.AddWithValue("scope", scopeKey);
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync() as string;
    }
}
