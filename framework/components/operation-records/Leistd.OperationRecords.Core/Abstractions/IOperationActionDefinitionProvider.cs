namespace Leistd.OperationRecords.Abstractions;

/// <summary>
/// 向定义上下文登记本模块的操作动作。
/// </summary>
/// <remarks>
/// <para><b>为什么动作码要注册，而不是散落的字符串常量。</b>框架原先只把动作码原样存下去、
/// 从不解释，代价是四件事做不到：界面无法按类别筛选（只能硬编码码表）、
/// 无法在构建期断言"每个动作都有中英文案"、无法区分一方码与第三方码、
/// 也无法按严重度接告警。登记之后这四件事都由定义本身回答。</para>
/// <para>动作码<b>一经发布就不要改</b>：它是历史记录的含义本身，改了等于篡改过去。</para>
/// </remarks>
/// <example>
/// <code>
/// public class AppOperationActionDefinitionProvider : IOperationActionDefinitionProvider
/// {
///     public void Define(IOperationActionDefinitionContext context)
///     {
///         context.Add("user.created", OperationCategories.Account, OperationVisibility.Tenant);
///         context.Add("permission-grants.replaced", OperationCategories.Authorization,
///             OperationVisibility.Tenant, OperationSeverity.Critical);
///     }
/// }
/// </code>
/// </example>
public interface IOperationActionDefinitionProvider
{
    /// <summary>登记操作动作定义。</summary>
    /// <param name="context">定义上下文。</param>
    void Define(IOperationActionDefinitionContext context);
}

/// <summary>
/// 操作动作的登记与查询上下文。
/// </summary>
/// <remarks>动作码在全局唯一，重复登记在启动期抛出异常——与权限定义同一约定。</remarks>
public interface IOperationActionDefinitionContext
{
    /// <summary>
    /// 登记一个操作动作。
    /// </summary>
    /// <param name="code">动作码，全局唯一且一经发布不可更改（如 <c>user.created</c>）。</param>
    /// <param name="category">
    /// 类别，驱动界面分类筛选；取值见 <see cref="OperationCategories"/>，业务可自定义。
    /// </param>
    /// <param name="visibility">
    /// 可见性，<b>必填</b>。没有默认值是刻意的：省略时静默落到"租户可见"，
    /// 而宿主侧动作落成租户可见就是跨租户信息泄露，且只在真的建了租户之后才暴露。
    /// 判据是这条记录描述的操作属于谁的边界。
    /// </param>
    /// <param name="severity">严重度，默认 <see cref="OperationSeverity.Info"/>。</param>
    /// <returns>登记后的定义。</returns>
    IOperationActionDefinition Add(
        string code,
        string category,
        OperationVisibility visibility,
        OperationSeverity severity = OperationSeverity.Info);

    /// <summary>按动作码查找定义；不存在时返回 <see langword="null"/>。</summary>
    IOperationActionDefinition? GetOrNull(string code);
}

/// <summary>
/// 一个已登记的操作动作定义。
/// </summary>
public interface IOperationActionDefinition
{
    /// <summary>动作码（全局唯一标识）。</summary>
    string Code { get; }

    /// <summary>类别。</summary>
    string Category { get; }

    /// <summary>可见性。</summary>
    OperationVisibility Visibility { get; }

    /// <summary>严重度。</summary>
    OperationSeverity Severity { get; }
}

/// <summary>
/// 提供操作动作定义的只读索引。
/// </summary>
public interface IOperationActionDefinitionManager
{
    /// <summary>按动作码查找；不存在时返回 <see langword="null"/>。</summary>
    /// <remarks>
    /// 返回 <see langword="null"/> 表示这是一个<b>未登记</b>的码。写入时它是编码错误，记录器直接抛出；
    /// 读取历史记录时遇到它（码后来被移除了），调用方应据此降级（界面原样显示裸码），而不是抛错：
    /// 审计记录是既成事实，不能因为定义缺失就让整页读不出来。
    /// </remarks>
    IOperationActionDefinition? GetOrNull(string code);

    /// <summary>获取全部定义，按登记顺序。</summary>
    IReadOnlyList<IOperationActionDefinition> GetAll();

    /// <summary>获取全部出现过的类别，用于驱动界面的分类筛选项。</summary>
    IReadOnlyList<string> GetCategories();
}
