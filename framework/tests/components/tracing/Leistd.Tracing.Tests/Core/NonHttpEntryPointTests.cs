using System.Diagnostics;
using Leistd.AmbientContext;
using Leistd.DependencyInjection.DynamicProxy.Registration;
using Leistd.Security;
using Leistd.Tracing.Abstractions;
using Leistd.Tracing.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Tracing.Tests.Core;

/// <summary>
/// 没有入站请求时的链路标识：环境上下文贡献者与 <c>[CorrelationId]</c> 拦截器。
/// </summary>
/// <remarks>
/// 链路追踪的价值恰恰在后台作业、消息消费者、Hub 调用里能续上标识。
/// 只测 HTTP 中间件等于只测了它最不容易出问题的那一半。
/// </remarks>
public class NonHttpEntryPointTests
{
    private static ServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAmbientContext();
        services.AddCorrelationIdCore(new ConfigurationBuilder().Build());
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Ambient_scope_establishes_a_correlation_id_where_there_was_none()
    {
        using var provider = Build();
        var correlation = provider.GetRequiredService<ICorrelationIdProvider>();

        Assert.Null(correlation.Get());

        using (provider.GetRequiredService<IAmbientContext>().Begin(new System.Security.Claims.ClaimsPrincipal()))
        {
            Assert.False(string.IsNullOrEmpty(correlation.Get()));
        }

        Assert.Null(correlation.Get());
    }

    // 调用方带来的标识（消息头、作业参数）必须被采纳，否则跨进程的链路会在这里断开。
    [Fact]
    public void Caller_supplied_correlation_id_is_adopted()
    {
        using var provider = Build();
        var correlation = provider.GetRequiredService<ICorrelationIdProvider>();
        const string Incoming = "0af7651916cd43dd8448eb211c80319c";

        using (provider.GetRequiredService<IAmbientContext>().Begin(
                   new System.Security.Claims.ClaimsPrincipal(), correlationId: Incoming))
        {
            Assert.Equal(Incoming, correlation.Get());
        }
    }

    // 有 Activity 时以它的 TraceId 为准，优先于调用方指定值——反过来会让同一次调用
    // 在 APM 与日志里出现两个标识，排障时对不上。
    [Fact]
    public void Activity_trace_id_wins_over_the_caller_supplied_value()
    {
        using var provider = Build();
        var correlation = provider.GetRequiredService<ICorrelationIdProvider>();

        using var listener = ListenToEverything();
        using var activity = new ActivitySource(nameof(NonHttpEntryPointTests)).StartActivity("job");
        Assert.NotNull(activity);

        using (provider.GetRequiredService<IAmbientContext>().Begin(
                   new System.Security.Claims.ClaimsPrincipal(), correlationId: "ffffffffffffffffffffffffffffffff"))
        {
            Assert.Equal(activity.TraceId.ToHexString(), correlation.Get());
        }
    }

    public interface IJob
    {
        Task<string?> RunAsync();
        Task RunVoidAsync();
        string? Observed { get; }
    }

    // 特性打在实现类上：注册回调按实现类型判定是否织入。
    [CorrelationId]
    public sealed class Job(ICorrelationIdProvider correlation) : IJob
    {
        public Task<string?> RunAsync() => Task.FromResult(correlation.Get());

        public Task RunVoidAsync()
        {
            Observed = correlation.Get();
            return Task.CompletedTask;
        }

        public string? Observed { get; private set; }
    }

    /// <remarks>织入经 <c>DynamicProxyServiceRegistrationCallbackFactory</c> 建容器，与工作单元的用例同口径。</remarks>
    private static ServiceProvider BuildWithJob()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAmbientContext();
        services.AddCorrelationIdCore(new ConfigurationBuilder().Build());
        services.AddSingleton<IJob, Job>();

        return (ServiceProvider)new DynamicProxyServiceRegistrationCallbackFactory().CreateServiceProvider(services);
    }

    // 拦截器为没有入站请求的调用建立标识：作业方法内部必须能读到值。
    [Fact]
    public async Task Attribute_creates_a_correlation_id_for_a_background_call()
    {
        using var provider = BuildWithJob();

        var observed = await provider.GetRequiredService<IJob>().RunAsync();

        Assert.False(string.IsNullOrEmpty(observed));
        Assert.Equal(32, observed!.Length);
    }

    // 无返回值的重载走另一条代码路径，必须分别覆盖。
    [Fact]
    public async Task Attribute_also_covers_methods_without_a_result()
    {
        using var provider = BuildWithJob();
        var job = provider.GetRequiredService<IJob>();

        await job.RunVoidAsync();

        Assert.False(string.IsNullOrEmpty(job.Observed));
    }

    // 已经有标识时不得换发：换发会把一次调用在日志里劈成两段链路。
    [Fact]
    public async Task Existing_correlation_id_is_kept_rather_than_replaced()
    {
        using var provider = BuildWithJob();
        var correlation = provider.GetRequiredService<ICorrelationIdProvider>();
        const string Existing = "0af7651916cd43dd8448eb211c80319c";

        using (correlation.Change(Existing))
        {
            Assert.Equal(Existing, await provider.GetRequiredService<IJob>().RunAsync());
        }
    }

    // 作用域退出后必须还原，否则同一个宿主里的下一次调用会沿用上一次的标识。
    [Fact]
    public async Task Correlation_id_does_not_outlive_the_call()
    {
        using var provider = BuildWithJob();
        var correlation = provider.GetRequiredService<ICorrelationIdProvider>();

        await provider.GetRequiredService<IJob>().RunAsync();

        Assert.Null(correlation.Get());
    }

    private static ActivityListener ListenToEverything()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
