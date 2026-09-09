namespace Leistd.Settings.Definitions;

/// <summary>
/// 设置值允许存放的层级。
/// </summary>
/// <remarks>
/// 代码默认值不在其中：它由 <c>ISettingDefinition.DefaultValue</c> 承载，任何设置都有。
/// </remarks>
[Flags]
public enum SettingScopes
{
    /// <summary>不允许覆盖，只能用代码默认值。</summary>
    None = 0,

    /// <summary>租户级；宿主视角（<c>TenantId</c> 为 <see langword="null"/>）也走这一层。</summary>
    Tenant = 1,

    /// <summary>用户级，优先于租户级。</summary>
    User = 2,

    /// <summary>租户级与用户级都允许。</summary>
    All = Tenant | User,

    /// <summary>
    /// 仅宿主级：整个进程只有一份值，租户与用户都不能覆盖。
    /// </summary>
    /// <remarks>
    /// 给的是<b>进程级</b>的东西——日志级别这类一个进程只有一份的配置。
    /// 它和 <see cref="Tenant"/> 不是一回事：租户级是"每个租户各有一份默认值"，
    /// 而这里按租户各存一份根本无从生效，写进去也只是让界面显示一个不起作用的值。
    /// <para>
    /// 因此它<b>不与其它层级组合</b>：一项设置要么是进程级的，要么是可分层覆盖的，
    /// 中间态没有意义。写入只允许发生在宿主上下文，租户上下文的写入会被拒绝。
    /// </para>
    /// </remarks>
    Host = 4
}
