using Leistd.EventBus.Events;

namespace CompanyName.ProjectName.Application.Settings.Events;

/// <summary>
/// 宿主级设置被改写。
/// </summary>
/// <remarks>在工作单元内发布时推迟到事务提交后分发，处理器据此把新值应用到本进程。</remarks>
/// <param name="settingName">被改写的设置名。</param>
public sealed class HostSettingChangedEvent(string settingName) : LocalEvent
{
    /// <summary>被改写的设置名。</summary>
    public string SettingName { get; } = settingName;
}
