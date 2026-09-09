using CompanyName.ProjectName.Application.Settings.Hosting;
using Serilog.Core;
using Serilog.Events;

namespace CompanyName.ProjectName.Api.Logging;

/// <summary>
/// 日志设置的进程内可变状态
/// </summary>
/// <remarks>
/// 单例。日志级别用 Serilog 的 <see cref="LoggingLevelSwitch"/> 承载——那是 Serilog
/// 给运行期改级别的正规入口：<c>MinimumLevel.ControlledBy(switch)</c> 之后改它的
/// <see cref="LoggingLevelSwitch.MinimumLevel"/> 立即生效，不必重建 logger、
/// 也不必让配置源支持重载。
/// <para>
/// 请求日志级别单独放一个字段而不是第二个开关：它不是"最小级别"，
/// 而是"正常完成的请求记成哪一级"，由 <c>UseSerilogRequestLogging</c> 的回调按请求读取。
/// </para>
/// <para>
/// 两个初始值都来自<b>部署基线</b>（<see cref="LoggingBaseline"/>），与设置定义的默认值同源：
/// 写死成 Information 的话，部署把 Serilog 的最小级别配成 Warning 也会在这里被顶掉，
/// 而库里还没有任何覆盖值——那不是"运行期改过"，只是基线被无声丢弃了。
/// </para>
/// </remarks>
/// <param name="baseline">部署配置提供的日志基线。</param>
public sealed class LoggingSettingState(LoggingBaseline baseline)
{
    /// <summary>全局最小级别开关，交给 Serilog 的 <c>MinimumLevel.ControlledBy</c>。</summary>
    public LoggingLevelSwitch MinimumLevel { get; } = new(Parse(baseline.MinimumLevel));

    /// <summary>
    /// 正常完成的请求记成哪一级。
    /// </summary>
    /// <remarks>
    /// 用 <c>volatile</c> 读写：写入发生在处理设置写请求的那个线程，
    /// 读取发生在其它请求线程上，没有内存屏障时新值可能迟迟看不见。
    /// </remarks>
    public LogEventLevel RequestLevel
    {
        get => (LogEventLevel)Volatile.Read(ref _requestLevel);
        set => Volatile.Write(ref _requestLevel, (int)value);
    }

    private int _requestLevel = (int)Parse(baseline.RequestLevel);

    // 基线已在 LoggingBaseline 里归一到 Serilog 的级别名，这里只是把字符串落到枚举上；
    // 仍留一层兜底，免得某天基线的来源变了就静默按 0（Verbose）启动。
    private static LogEventLevel Parse(string level) =>
        Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : LogEventLevel.Information;
}
