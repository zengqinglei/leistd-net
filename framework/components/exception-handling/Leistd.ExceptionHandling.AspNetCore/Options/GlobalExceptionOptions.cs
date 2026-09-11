namespace Leistd.ExceptionHandling.AspNetCore.Options;

/// <summary>
/// 配置全局异常响应。
/// </summary>
public class GlobalExceptionOptions
{
    /// <summary>
    /// 获取或设置是否启用全局异常处理。
    /// </summary>
    /// <remarks>
    /// 调用 <c>AddGlobalExceptionHandler()</c> 本身就是启用意图，因此默认为真；
    /// 需要临时关闭（例如排查中间件顺序）时显式配 <see langword="false"/>。
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 排除的URI模式（如：/api/health/**）
    /// </summary>
    public HashSet<string> ExcludePatterns { get; set; } = [];

    /// <summary>
    /// 是否在错误响应中包含业务诊断详情和异常堆栈。默认 <see langword="false"/>。
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    /// <summary>
    /// 词条未命中时，业务异常的 <c>Message</c> 呈现给终端用户的范围。默认 <see cref="BusinessMessageExposure.ClientErrors"/>。
    /// </summary>
    /// <remarks>
    /// <b>默认就把原因说出来</b>：4xx 的消息直出，5xx 的不直出。错误原因是产品行为的一部分，
    /// 不该因为"某个抛出点忘了声明"而变成一句无信息量的通用话；而真正需要藏起来的是 5xx 的
    /// 内部诊断，那按状态码类别一刀切就够了，不必让每个调用点各自判断。
    /// <para>
    /// 有词条时仍以词条优先（见处理器的解析顺序），本项只管"没有词条可用"的那一档。
    /// 诊断详情与堆栈由 <see cref="IncludeExceptionDetails"/> 独立控制，二者互不影响。
    /// </para>
    /// <para>本项经 <c>IOptionsMonitor</c> 读取，改配置即时生效，不必重启。</para>
    /// </remarks>
    public BusinessMessageExposure MessageExposure { get; set; } = BusinessMessageExposure.ClientErrors;
}
