#if (RemoteTokenAuth)
extern alias Migrator;
using System.Diagnostics;
using DatabaseMigrationRunner = Migrator::CompanyName.ProjectName.DbMigrator.DatabaseMigrationRunner;
using Npgsql;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>正式管理员引导入口：真实 PostgreSQL、真实 CLI、投影前授予与撤销保留。</summary>
public sealed class ResourceAdminBootstrapTests
{
    [Fact]
    public async Task Dry_run_is_read_only_and_apply_is_idempotent_without_resurrecting_revocations()
    {
        var connection = PostgreSqlTestDatabase.CreateDatabase();
        try
        {
            var subject = Guid.NewGuid();
            var before = await StateAsync(connection);
            var dry = await RunAsync(connection, "--grant-admin", subject.ToString());
            Assert.True(dry.Exit == 0, dry.Output);
            Assert.Contains("Dry run", dry.Output);
            Assert.Equal(0, await GrantCountAsync(connection, subject));
            Assert.Equal(before, await StateAsync(connection));

            var applied = await RunAsync(connection, "--grant-admin", subject.ToString(), "--apply");
            Assert.True(applied.Exit == 0, applied.Output);
            Assert.Contains("Granted", applied.Output);
            Assert.True(await GrantCountAsync(connection, subject) > 0);
            var after = await StateAsync(connection);
            Assert.Equal(before.Users, after.Users); // 无用户外键，也不创建伪造投影。
#if (IncludeOperationRecords)
            Assert.Equal(before.Records + 1, after.Records);
#else
            Assert.Contains("resource.admin-granted", applied.Output);
            Assert.Contains("deployment:", applied.Output);
            Assert.Contains("DeploymentBootstrap", applied.Output);
#endif
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var revoke = db.CreateCommand();
            // 模拟之后明确撤销：只删除授予，不重置首次写入的授权版本。
            revoke.CommandText = "DELETE FROM \"companyname-projectname\".\"PermissionGrantRecords\" WHERE \"ProviderName\" = 'User' AND \"ProviderKey\" = @subject";
            revoke.Parameters.AddWithValue("subject", subject.ToString());
            await revoke.ExecuteNonQueryAsync();
            var repeated = await RunAsync(connection, "--grant-admin", subject.ToString(), "--apply");
            Assert.True(repeated.Exit == 0, repeated.Output);
            Assert.Contains("already has grant history", repeated.Output);
            Assert.Equal(0, await GrantCountAsync(connection, subject));
        }
        finally
        {
            PostgreSqlTestDatabase.DropDatabase(connection);
        }
    }
#if (IncludeOperationRecords)

    [Fact]
    public async Task A_rejected_audit_insert_rolls_back_all_bootstrap_grants_and_versions()
    {
        var connection = PostgreSqlTestDatabase.CreateDatabase();
        try
        {
            var before = await StateAsync(connection);
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var reject = db.CreateCommand();
            reject.CommandText = "ALTER TABLE \"companyname-projectname\".\"OperationRecords\" ADD CONSTRAINT reject_bootstrap CHECK (\"Action\" <> 'resource.admin-granted')";
            await reject.ExecuteNonQueryAsync();
            var applied = await RunAsync(connection, "--grant-admin", Guid.NewGuid().ToString(), "--apply");
            Assert.NotEqual(0, applied.Exit);
            Assert.Equal(before, await StateAsync(connection));
        }
        finally { PostgreSqlTestDatabase.DropDatabase(connection); }
    }
#endif

    private static async Task<(long Users, long Grants, long Versions, long Records)> StateAsync(string connection)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT (SELECT count(*) FROM \"companyname-projectname\".\"Users\"), "
            + "(SELECT count(*) FROM \"companyname-projectname\".\"PermissionGrantRecords\"), "
            + "(SELECT count(*) FROM \"companyname-projectname\".\"AuthorizationVersionRecord\"), "
#if (IncludeOperationRecords)
            + "(SELECT count(*) FROM \"companyname-projectname\".\"OperationRecords\")";
#else
            + "0::bigint";
#endif
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static async Task<long> GrantCountAsync(string connection, Guid subject)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT count(*) FROM \"companyname-projectname\".\"PermissionGrantRecords\" WHERE \"ProviderName\" = 'User' AND \"ProviderKey\" = @subject";
        command.Parameters.AddWithValue("subject", subject.ToString());
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(int Exit, string Output)> RunAsync(string connection, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CompanyName.ProjectName.IntegrationTests.runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CompanyName.ProjectName.IntegrationTests.deps.json"));
        start.ArgumentList.Add(typeof(DatabaseMigrationRunner).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__Default"] = connection;
        start.Environment["ConnectionStrings__Redis"] = "";
#if (IncludeMultiTenancy)
        start.Environment["Leistd__ServiceClients__Identity__BaseAddress"] = "https://identity.example";
#endif
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
#endif
