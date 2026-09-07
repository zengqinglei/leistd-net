using Leistd.ExceptionHandling;

namespace Leistd.Settings.Exceptions;

/// <summary>
/// 表示读写了未定义的设置。
/// </summary>
/// <remarks>
/// 与"值为空"刻意分开：未定义意味着名字拼错或漏注册 <c>ISettingDefinitionProvider</c>，
/// 静默回落到默认值会让这两种情况看起来一样。
/// </remarks>
/// <param name="settingName">未定义的设置名。</param>
public class UndefinedSettingException(string settingName)
    : BadRequestException($"Setting '{settingName}' is not defined. Register an ISettingDefinitionProvider that defines it.")
{
    /// <summary>未定义的设置名。</summary>
    public string SettingName { get; } = settingName;
}
