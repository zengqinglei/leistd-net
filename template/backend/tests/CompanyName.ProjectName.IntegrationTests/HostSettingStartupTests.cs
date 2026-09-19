using CompanyName.ProjectName.Application.Settings.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 宿主级设置在宿主开始接收请求之前就已应用：最早的请求与日志用的是设置里的值，而不是部署配置。
/// </summary>
/// <remarks>
/// 托管服务都启动完宿主才开始接收请求（Web 服务器的托管服务排在最后）。首次应用若只是在后台开跑，
/// 宿主照样启动完成、开始接请求，而设置要等后台那一轮跑完才生效——平时看不出来，因为那一轮通常很快。
/// </remarks>
public sealed class HostSettingStartupTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task First_apply_completes_before_the_host_starts()
    {
        var probe = new FirstApplyProbe();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<IHostSettingApplier, StartupObservingApplier>();
        }));

        _ = host.Services;

        var startedFirst = await probe.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(startedFirst, "The first host setting apply completed only after the host had started serving.");
    }

    private sealed class FirstApplyProbe
    {
        private int _observed;

        public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryClaimFirst() => Interlocked.Exchange(ref _observed, 1) == 0;
    }

    // 首次应用时等"宿主已启动"信号，等不到就在短暂超时后放行，然后记下完成那一刻宿主是否已经启动。
    // 启动路径上的应用会挡住宿主启动，信号不会来，记下 false；后台开跑的应用等到信号，记下 true。
    private sealed class StartupObservingApplier(FirstApplyProbe probe, IHostApplicationLifetime lifetime)
        : IHostSettingApplier
    {
        public async Task ApplyAsync(CancellationToken cancellationToken = default)
        {
            if (!probe.TryClaimFirst())
            {
                return;
            }

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
            {
                await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
            }

            probe.Completed.TrySetResult(lifetime.ApplicationStarted.IsCancellationRequested);
        }
    }
}
