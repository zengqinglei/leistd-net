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

    /// <summary>
    /// 是否为机密设置：值落库前加密，读取时解密。
    /// </summary>
    /// <remarks>
    /// <para>用于口令、令牌一类的值：数据库单独泄漏时不应连带泄漏它们。加解密用宿主的 Data Protection
    /// （<c>IDataProtectionProvider</c>）——框架不持有密钥，宿主没注册 Data Protection 时，
    /// 第一次写入或读取机密设置就会失败，而不是悄悄存成明文。</para>
    /// <para>机密设置<b>永不下发客户端</b>：即使同时标记了 <see cref="IsVisibleToClients"/>，
    /// <see cref="ISettingProvider.GetAllAsync"/> 按"仅客户端可见"读取时也不包含它。
    /// 可见标记只表示"界面上有这一项可以写"，宿主据此给出只写的输入框。</para>
    /// <para>代码默认值不加密：机密设置的默认值应当为空，由宿主在未设置时自行回落（例如回落到配置文件）。</para>
    /// </remarks>
    bool IsEncrypted { get; set; }

    /// <summary>值的类型，写入时据此校验。</summary>
    /// <remarks>
    /// 默认 <see cref="SettingValueType.Text"/>。值域在写入端把关而不是只靠界面：
    /// 脚本、旧版客户端与迁移数据都绕得过界面，非法值一旦落库，之后每个消费方都得自己防御。
    /// 界面也读它渲染控件（开关、带上下界的数字框、下拉框），两边同源不会各走一边。
    /// </remarks>
    SettingValueType ValueType { get; set; }

    /// <summary>整数设置的下界（含）；<see langword="null"/> 表示不限。</summary>
    int? Minimum { get; set; }

    /// <summary>整数设置的上界（含）；<see langword="null"/> 表示不限。</summary>
    int? Maximum { get; set; }

    /// <summary>
    /// 允许的取值（按序号比较）；<see langword="null"/> 表示不限。
    /// </summary>
    /// <remarks>写入端据此拒绝候选之外的值，界面据此渲染下拉框。</remarks>
    IReadOnlyList<string>? AllowedValues { get; set; }
}
