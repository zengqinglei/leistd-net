using System.Diagnostics;
using Leistd.AmbientContext;
using Leistd.BackgroundJobs.InProcess;
using Leistd.BackgroundJobs.InProcess.Recurring;
using Leistd.BackgroundJobs.Recurring;
using Leistd.Lock.Memory;
using Leistd.Security;
using Leistd.Security.Claims;
using Leistd.Tracing;
using Leistd.Tracing.Abstractions;
using Leistd.Tracing.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Leistd.BackgroundJobs.Tests;

/// <summary>
/// 周期任务的一次执行在环境上下文里进行：日志带 <c>leistd.correlationId</c>，取值是本次执行的 TraceId，
/// 与 HTTP 请求、后台队列用同一个键检索；执行结束即撤销，不串到下一次或宿主自身的日志。
/// </summary>
public sealed class RecurringJobCorrelationTests : IDisposable
{
    private readonly List<(string Message, object? Correlation)> _logged = [];
    private readonly RunLog _runs = new();
    private readonly ServiceProvider _provider;

    public RecurringJobCorrelationTests() => _provider = Build();

    private ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(new ScopeRecordingLoggerProvider(_logged)))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddMemoryLocalLock()
            .AddInProcessBackgroundJobs()
            .AddRecurringJob<LoggingJob>("test.correlated.cluster", RecurringJobSchedule.Every(TimeSpan.FromMinutes(5)), RecurringJobScope.Cluster)
            .AddRecurringJob<LoggingJob>("test.correlated.instance", RecurringJobSchedule.Every(TimeSpan.FromMinutes(5)), RecurringJobScope.EveryInstance)
            .AddRecurringJob<ThrowingJob>("test.correlated.failing", RecurringJobSchedule.Every(TimeSpan.FromMinutes(5)), RecurringJobScope.Cluster)
            .AddRecurringJob<LoggingJob>(
                "test.correlated.switch-fails",
                RecurringJobSchedule.Every(TimeSpan.FromMinutes(5)),
                RecurringJobScope.Cluster,
                isEnabled: scope =>
                {
                    scope.GetRequiredService<RunLog>().SwitchTraceId = Activity.Current?.TraceId.ToHexString();
                    throw new InvalidOperationException("switch failed");
                });
        services.AddAmbientContext();
        services.AddCorrelationIdCore();
        services.AddSingleton(_runs);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("test.correlated.cluster")]
    [InlineData("test.correlated.instance")]
    public async Task The_jobs_logs_carry_the_runs_trace_id_as_correlation_id(string name)
    {
        await RunAsync(name);

        var run = Assert.Single(_runs.Runs);
        Assert.False(string.IsNullOrEmpty(run.TraceId));
        Assert.Equal(run.TraceId, run.CorrelationId);
        Assert.Equal(run.TraceId, _logged.Single(entry => entry.Message == "job running").Correlation);
        // 周期任务没有主体：作用域里是空主体，不继承调度线程上残留的主体
        Assert.False(run.Authenticated);
    }

    // 任务失败时调度器自己的失败日志同样带上，排障时与任务日志按同一个键串起来
    [Fact]
    public async Task The_runners_failure_log_carries_the_same_correlation_id()
    {
        await RunAsync("test.correlated.failing");

        var run = Assert.Single(_runs.Runs);
        Assert.Equal(run.TraceId, _logged.Single(entry => entry.Message.StartsWith("Recurring job test.correlated.failing failed", StringComparison.Ordinal)).Correlation);
    }

    // 开始之前的故障（如开关求值抛错）在环境上下文里分类与记日志：调度器的失败日志同样带本轮的标识
    [Fact]
    public async Task A_failure_before_the_job_starts_is_logged_with_the_runs_correlation_id()
    {
        await RunAsync("test.correlated.switch-fails");

        Assert.Empty(_runs.Runs);
        Assert.NotNull(_runs.SwitchTraceId);
        Assert.Equal(_runs.SwitchTraceId, _logged.Single(entry => entry.Message.StartsWith("Recurring job test.correlated.switch-fails failed", StringComparison.Ordinal)).Correlation);
    }

    // 执行结束即撤销：之后宿主自身的日志与下一次执行都不带上一次的标识
    [Fact]
    public async Task The_scope_ends_with_the_run()
    {
        await RunAsync("test.correlated.instance");
        await RunAsync("test.correlated.instance");
        _provider.GetRequiredService<ILogger<RecurringJobCorrelationTests>>().LogWarning("after the runs");

        Assert.Null(_logged.Single(entry => entry.Message == "after the runs").Correlation);
        var correlations = _logged.Where(entry => entry.Message == "job running").Select(entry => entry.Correlation).ToArray();
        Assert.Equal(2, correlations.Length);
        Assert.NotEqual(correlations[0], correlations[1]);
    }

    // 贡献者可以按 Scoped 注册（依赖本轮的作用域服务）：环境上下文从本轮作用域取，启用作用域检查也照常执行
    [Fact]
    public async Task A_scoped_contributor_still_lets_the_job_run()
    {
        using var provider = Build(services =>
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IAmbientContextContributor, ScopedContributor>()));

        Assert.Equal(RecurringJobRunnerResult.Completed, await RunAsync("test.correlated.instance", provider));
        Assert.Single(_runs.Runs);
    }

    // 退出环境上下文时某个维度抛错：本轮仍按成功结束（不冒出执行器、不停掉调度循环），只记一条警告
    [Fact]
    public async Task A_failure_leaving_the_ambient_context_does_not_escape_the_run()
    {
        using var provider = Build(services =>
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IAmbientContextContributor, ThrowOnLeaveContributor>()));

        Assert.Equal(RecurringJobRunnerResult.Completed, await RunAsync("test.correlated.cluster", provider));
        Assert.Equal(RecurringJobRunnerResult.SkippedCompleted, await RunAsync("test.correlated.cluster", provider));
        Assert.Single(_runs.Runs);
        Assert.Contains(_logged, entry => entry.Message == "Recurring job test.correlated.cluster failed to leave its ambient context.");
    }

    private Task RunAsync(string name) => RunAsync(name, _provider);

    private static Task<RecurringJobRunnerResult> RunAsync(string name, IServiceProvider provider)
    {
        var definition = provider.GetServices<RecurringJobDefinition>().Single(d => d.Name == name);
        return provider.GetRequiredService<RecurringJobRunner>()
            .RunOnceAsync(definition, definition.ScheduleFactory(provider), CancellationToken.None);
    }

    private sealed class ScopedContributor : IAmbientContextContributor
    {
        public IDisposable? Enter(AmbientContextEnterContext context) => null;
    }

    private sealed class ThrowOnLeaveContributor : IAmbientContextContributor
    {
        public IDisposable? Enter(AmbientContextEnterContext context) => new ThrowOnDispose();

        private sealed class ThrowOnDispose : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("leave failed");
        }
    }

    private sealed record Run(string? TraceId, string? CorrelationId, bool Authenticated);

    private sealed class RunLog
    {
        public List<Run> Runs { get; } = [];

        public string? SwitchTraceId { get; set; }
    }

    private sealed class LoggingJob(
        ILogger<LoggingJob> logger,
        ICorrelationIdProvider correlation,
        ICurrentPrincipalAccessor principal,
        RunLog runs) : IRecurringJob
    {
        public Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
        {
            runs.Runs.Add(new Run(
                Activity.Current?.TraceId.ToHexString(),
                correlation.Get(),
                principal.Principal?.Identity?.IsAuthenticated == true));
            logger.LogWarning("job running");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingJob(RunLog runs) : IRecurringJob
    {
        public Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
        {
            runs.Runs.Add(new Run(Activity.Current?.TraceId.ToHexString(), null, false));
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class ScopeRecordingLoggerProvider(List<(string Message, object? Correlation)> logged)
        : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopes;

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        private void Record(string message, object? correlation)
        {
            lock (logged)
            {
                logged.Add((message, correlation));
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(ScopeRecordingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes?.Push(state);

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                object? correlation = null;
                owner._scopes?.ForEachScope((scope, _) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var pair in pairs)
                        {
                            if (pair.Key == CorrelationIdConstants.LogKey)
                            {
                                correlation = pair.Value;
                            }
                        }
                    }
                }, (object?)null);

                owner.Record(formatter(state, exception), correlation);
            }
        }
    }
}
