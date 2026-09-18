using CompanyName.ProjectName.Application.Settings.Hosting;
using Leistd.MultiTenancy.Abstractions;
using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.HostedServices.BackgroundJobs;

/// <summary>
/// 周期性把宿主级设置推到进程内状态上
/// </summary>
/// <remarks>
/// 存在的理由是<b>多实例</b>：设置行是共享的，进程内状态不是。A 实例上改完日志级别，
/// B 实例并不知道——只靠"写入后立即应用"的话，改动只在恰好处理了那次写请求的实例上生效，
/// 表现成"改了但一半日志没变"。这里按固定周期重读一次，代价是一条按主键的行查询。
/// <para>
/// 启动时先在 <see cref="StartAsync"/> 里同步跑一次，把库里的值接上来：托管服务都启动完宿主才开始接收请求，
/// 与官方配置提供程序在构建时加载一致，最早的请求与日志用的就已经是设置里的值。此时库可能还没迁移
/// （全新部署的第一次启动），因此失败只记录不抛——拿不到就用部署配置，不该拦住整个应用启动。
/// </para>
/// <para>
/// 刷新周期刻意留在 <c>appsettings</c> 而不是做成设置项：它决定"设置多久生效"，
/// 自己做成设置就成了自举依赖——把它改错还得靠它自己来纠正。
/// </para>
/// </remarks>
/// <param name="scopeFactory">每轮开一个作用域：设置解析是 Scoped 且按请求记忆化。</param>
/// <param name="options">刷新周期。</param>
/// <param name="logger">用于报告刷新失败。</param>
public sealed class HostSettingRefreshJob(
    IServiceScopeFactory scopeFactory,
    IOptions<HostSettingRefreshOptions> options,
    ILogger<HostSettingRefreshJob> logger) : BackgroundService
{
    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await ApplyOnceAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);

        while (await WaitAsync(timer, stoppingToken))
        {
            await ApplyOnceAsync(stoppingToken);
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ApplyOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            // 宿主上下文：宿主级设置只有宿主那一行，租户上下文下读到的是空
            // （查询过滤器会滤掉它），而后台任务本来就没有租户上下文。
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(null))
            {
                foreach (var applier in scope.ServiceProvider.GetServices<IHostSettingApplier>())
                {
                    await applier.ApplyAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 启动被取消或停机中，正常退出
        }
        catch (Exception ex)
        {
            // 库还没迁移、连接暂时不可用等：保留当前进程内状态，下一轮再试。
            logger.LogWarning(ex, "Refreshing host settings failed; keeping the current values.");
        }
    }
}
