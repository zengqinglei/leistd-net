using Leistd.ExceptionHandling;
using Leistd.Settings.Abstractions;

namespace Leistd.Settings.Exceptions;

/// <summary>
/// 表示读写了未定义的设置。
/// </summary>
/// <remarks>
/// 与"值为空"刻意分开：未定义意味着名字拼错或漏注册 <c>ISettingDefinitionProvider</c>，
/// 静默回落到默认值会让这两种情况看起来一样。错误码为 <see cref="SettingErrorCodes.Undefined"/>。
/// </remarks>
public class UndefinedSettingException : BadRequestException
{
    /// <summary>以未定义的设置名构造。</summary>
    /// <param name="settingName">未定义的设置名。</param>
    public UndefinedSettingException(string settingName)
        : base($"Setting '{settingName}' is not defined. Register an ISettingDefinitionProvider that defines it.")
    {
        SettingName = settingName;
        WithCode(SettingErrorCodes.Undefined).WithData("Name", settingName);
    }

    /// <summary>未定义的设置名。</summary>
    public string SettingName { get; }
}
