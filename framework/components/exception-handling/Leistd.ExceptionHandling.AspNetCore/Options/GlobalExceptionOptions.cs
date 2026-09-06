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
    /// 获取或设置词条未命中时是否回落到 <c>Exception.Message</c>。
    /// </summary>
    /// <remarks>
    /// 默认只有显式 <c>BusinessException.AsUserFacing()</c> 的异常才直出 <c>Message</c> 原文，
    /// 其余一律使用状态码通用文案（<c>Error:NotFound</c> 等）。<c>Message</c> 无论如何都进日志。
    /// 置为 <see langword="true"/> 恢复无条件直出：仅适用于内部系统，且要接受未配词条的异常把运维英文暴露给终端用户。
    /// 诊断详情与堆栈由 <see cref="IncludeExceptionDetails"/> 独立控制。
    /// </remarks>
    public bool FallbackToExceptionMessage { get; set; }
}
