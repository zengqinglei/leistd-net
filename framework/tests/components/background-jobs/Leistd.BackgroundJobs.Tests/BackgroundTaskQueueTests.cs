using Leistd.AmbientContext;
using Leistd.BackgroundJobs.InProcess;
using Leistd.BackgroundJobs.InProcess.Options;
using Leistd.BackgroundJobs.InProcess.Queues;
using Leistd.BackgroundJobs.Queues;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.BackgroundJobs.Tests;

/// <summary>
/// 进程内队列：有界背压、入队时的上下文在执行时还原、单项失败不影响后续工作项。
/// </summary>
public sealed class BackgroundTaskQueueTests
{
    private static ServiceProvider Build(int capacity = 16, IAmbientContext? ambient = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddInProcessBackgroundJobs();
        services.Configure<InProcessBackgroundJobOptions>(o => o.QueueCapacity = capacity);
        if (ambient is not null)
        {
            services.AddSingleton(ambient);
        }

        return services.BuildServiceProvider();
    }

    private static BackgroundTaskQueueWorker Worker(IServiceProvider provider)
        => provider.GetServices<IHostedService>().OfType<BackgroundTaskQueueWorker>().Single();

    private static async Task DrainAsync(IServiceProvider provider)
    {
        var queue = provider.GetRequiredService<BackgroundTaskQueue>();
        while (queue.Reader.TryRead(out var item))
        {
            await Worker(provider).RunAsync(item, CancellationToken.None);
        }
    }

    /// <summary>满了就告诉调用方，而不是静默丢弃或无限堆积。</summary>
    [Fact]
    public void TryQueue_reports_a_full_queue()
    {
        using var provider = Build(capacity: 1);
        var queue = provider.GetRequiredService<IBackgroundTaskQueue>();

        Assert.True(queue.TryQueue((_, _) => ValueTask.CompletedTask));
        Assert.False(queue.TryQueue((_, _) => ValueTask.CompletedTask));
    }

    /// <summary>
    /// 入队时捕获的上下文在执行时还原：后台任务读到的是入队请求的主体、租户与链路标识
    /// </summary>
    /// <remarks>执行流是消费者自己的，不做还原的话任务在空上下文里运行，写下的数据归属与审计操作人都会丢失。</remarks>
    [Fact]
    public async Task The_context_captured_at_enqueue_is_restored_while_the_item_runs()
    {
        var ambient = new RecordingAmbientContext();
        using var provider = Build(ambient: ambient);
        var queue = provider.GetRequiredService<IBackgroundTaskQueue>();

        ambient.Current = "enqueued";
        string? seen = null;
        await queue.QueueAsync((_, _) =>
        {
            seen = ambient.Current;
            return ValueTask.CompletedTask;
        });
        ambient.Current = "later";

        await DrainAsync(provider);

        Assert.Equal("enqueued", seen);
        Assert.Equal("later", ambient.Current);
    }

    [Fact]
    public async Task A_failing_item_does_not_stop_the_next_one()
    {
        using var provider = Build();
        var queue = provider.GetRequiredService<IBackgroundTaskQueue>();
        var ran = false;

        await queue.QueueAsync((_, _) => throw new InvalidOperationException("boom"));
        await queue.QueueAsync((_, _) =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        await DrainAsync(provider);

        Assert.True(ran);
    }

    private sealed class RecordingAmbientContext : IAmbientContext
    {
        public string? Current { get; set; }

        public IDisposable Begin(System.Security.Claims.ClaimsPrincipal principal, string? correlationId = null)
            => throw new NotSupportedException();

        public AmbientContextSnapshot Capture()
            => new(null, new Dictionary<Type, object?> { [typeof(string)] = Current });

        public IDisposable Restore(AmbientContextSnapshot snapshot)
        {
            var previous = Current;
            Current = (string?)snapshot.States[typeof(string)];
            return new Restorer(() => Current = previous);
        }

        private sealed class Restorer(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
