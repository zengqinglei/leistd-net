using Leistd.ExceptionHandling;
using Leistd.Settings.Definitions;

namespace Leistd.Settings.Exceptions;

/// <summary>
/// 表示把设置写入了它的定义未允许的层级。
/// </summary>
/// <remarks>
/// 由写入方校验设置定义允许的层级。
/// </remarks>
/// <param name="settingName">设置名。</param>
/// <param name="scope">被拒绝的层级。</param>
/// <param name="allowed">定义允许的层级。</param>
public class SettingScopeNotAllowedException(string settingName, SettingScopes scope, SettingScopes allowed)
    : BadRequestException($"Setting '{settingName}' cannot be written at scope '{scope}'; it allows '{allowed}'.")
{
    /// <summary>设置名。</summary>
    public string SettingName { get; } = settingName;

    /// <summary>被拒绝的层级。</summary>
    public SettingScopes Scope { get; } = scope;

    /// <summary>定义允许的层级。</summary>
    public SettingScopes Allowed { get; } = allowed;
}
