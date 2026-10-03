#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.BackgroundJobs;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Data.Connections;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.Timing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CompanyName.ProjectName.UnitTests.Application;

/// <summary>逐库执行已经隔离的失败仍须冒给调度器，否则它会错误记下完成水位。</summary>
public sealed class ExpiredUserSessionCleanupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_databases_or_unresolved_tenants_fail_the_cleanup_run(bool failedDatabase)
    {
        var result = new TenantDatabaseRunResult(1,
            failedDatabase ? [TenantDatabase.ForHost("host")] : [],
            failedDatabase ? [] : [new TenantDatabaseFailure(Guid.NewGuid(), "unresolved")]);
        var runner = new ResultRunner(result);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateJob(runner).ExecuteAsync(
            new RecurringJobContext(ExpiredUserSessionCleanupJob.Name, DateTimeOffset.UnixEpoch), CancellationToken.None));

        Assert.Equal(ConnectionStringNames.Default, runner.ConnectionName);
        Assert.False(runner.ActiveOnly);
    }

    [Fact]
    public async Task A_successful_database_result_completes_the_cleanup_run()
    {
        var runner = new ResultRunner(new TenantDatabaseRunResult(1, [], []));
        using var cancellation = new CancellationTokenSource();

        await CreateJob(runner).ExecuteAsync(
            new RecurringJobContext(ExpiredUserSessionCleanupJob.Name, DateTimeOffset.UnixEpoch), cancellation.Token);

        Assert.Equal(cancellation.Token, runner.Token);
        Assert.Equal(ConnectionStringNames.Default, runner.ConnectionName);
        Assert.False(runner.ActiveOnly);
    }

    private static ExpiredUserSessionCleanupJob CreateJob(ITenantDatabaseRunner runner)
    {
        var clock = new UtcClockProvider(new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        // 这里只处理执行器返回的汇总，不进入数据库回调；SQL 与工作单元由集成测试验证。
        return new ExpiredUserSessionCleanupJob(runner, null!, null!, null!,
            Options.Create(new UserSessionOptions()), clock, NullLogger<ExpiredUserSessionCleanupJob>.Instance);
    }

    private sealed class ResultRunner(TenantDatabaseRunResult result) : ITenantDatabaseRunner
    {
        public string? ConnectionName { get; private set; }
        public bool ActiveOnly { get; private set; } = true;
        public CancellationToken Token { get; private set; }

        public Task<TenantDatabaseRunResult> ForEachDatabaseAsync(string connectionStringName, bool activeOnly,
            Func<TenantDatabase, CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            ConnectionName = connectionStringName;
            ActiveOnly = activeOnly;
            Token = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
#endif
