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
    /// 所属分组的稳定标识；<see langword="null"/> 表示未分组。
    /// </summary>
    /// <remarks>
    /// 只承载<b>分组标识</b>，不承载分组的展示文案——与 <see cref="DisplayName"/> 同一理由：
    /// 定义一次性加载并缓存，拿不到请求 culture。宿主按这个标识去查自己的词条表。
    /// <para>
    /// 分组是<b>信息架构</b>，不是控件元数据：设置多起来之后，界面需要按关注点把它们分开摆，
    /// 而"哪些设置属于同一件事"只有定义方知道。放在客户端另抄一份，
    /// 新增设置忘了登记就会落在界面之外——没有报错，只是没人看得见。
    /// </para>
    /// </remarks>
    string? Group { get; set; }

    /// <summary>
    /// 是否允许下发到客户端。
    /// </summary>
    /// <remarks>
    /// 默认 <see langword="false"/>：设置值可能是运维参数或内部阈值，
    /// 面向终端用户的接口只应返回显式标记为可见的项。
    /// </remarks>
    bool IsVisibleToClients { get; set; }
}
