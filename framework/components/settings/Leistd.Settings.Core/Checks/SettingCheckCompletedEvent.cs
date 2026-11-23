using Leistd.EventBus.Events;
using Leistd.Settings.Definitions;

namespace Leistd.Settings.Checks;

/// <summary>一项设置测试已执行并保存了结果；宿主可据此记操作记录。</summary>
/// <param name="settingName">主设置名。</param>
/// <param name="scope">测试所在的层级。</param>
/// <param name="passed">是否通过。</param>
/// <param name="failureCode">未通过时的失败码。</param>
public sealed class SettingCheckCompletedEvent(string settingName, SettingScopes scope, bool passed, string? failureCode)
    : LocalEvent
{
    /// <summary>主设置名。</summary>
    public string SettingName { get; } = settingName;

    /// <summary>测试所在的层级。</summary>
    public SettingScopes Scope { get; } = scope;

    /// <summary>是否通过。</summary>
    public bool Passed { get; } = passed;

    /// <summary>未通过时的失败码。</summary>
    public string? FailureCode { get; } = failureCode;
}
