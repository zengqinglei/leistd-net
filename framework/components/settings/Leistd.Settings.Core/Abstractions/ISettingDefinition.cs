using Leistd.Settings.Definitions;

namespace Leistd.Settings.Abstractions;

/// <summary>
/// 表示一项设置的定义。
/// </summary>
public interface ISettingDefinition
{
    /// <summary>设置名称（全局唯一）。</summary>
    string Name { get; }

    /// <summary>显示名称。</summary>
    /// <remarks>
    /// 框架原样返回，不做翻译：翻译需要请求 culture，而定义在首次访问时一次性加载并缓存。
    /// 宿主要本地化，就把这里当作回落文案，在下发给客户端时按自己的词条表翻译。
    /// 存本地化键会让没有本地化基建的宿主把内部标识直接摆到界面上。
    /// </remarks>
    string? DisplayName { get; set; }

    /// <summary>代码默认值；任何层级都没有值时返回它。</summary>
    string? DefaultValue { get; set; }

    /// <summary>允许覆盖该设置的层级。</summary>
    SettingScopes Scopes { get; }

    /// <summary>
    /// 是否允许下发到客户端。
    /// </summary>
    /// <remarks>
    /// 默认 <see langword="false"/>：设置值可能是运维参数或内部阈值，
    /// 面向终端用户的接口只应返回显式标记为可见的项。
    /// </remarks>
    bool IsVisibleToClients { get; set; }
}
