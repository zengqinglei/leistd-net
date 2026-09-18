using CompanyName.ProjectName.Application.Settings.Events;
using CompanyName.ProjectName.Application.Settings.Hosting;
using Leistd.EventBus.EventHandlers;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Application.Settings.EventHandlers;

/// <summary>
/// 宿主级设置改写后立即应用到本进程；其它实例由周期刷新跟上。
/// </summary>
/// <remarks>
/// <para>在事务提交后执行（本地事件处理器的默认阶段）：写入所在的事务若回滚，本进程不会先用上一个没落库的值。</para>
/// <para>应用失败只记告警：设置已经落库，此时报错只会让保存请求显得失败；下一轮周期刷新会再应用。</para>
/// </remarks>
internal sealed class HostSettingChangedEventHandler(
    IEnumerable<IHostSettingApplier> appliers,
    ILogger<HostSettingChangedEventHandler> logger) : IEventHandler<HostSettingChangedEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(HostSettingChangedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var applier in appliers)
            {
                await applier.ApplyAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Applying host setting {SettingName} failed; the next refresh will retry.", @event.SettingName);
        }
    }
}
