using System;

namespace Leistd.Timing;

/// <summary>
/// 时钟服务扩展方法，用于处理时区边界计算（对齐业务自然日）
/// </summary>
public static class ClockExtensions
{
    /// <summary>
    /// 获取当前系统本地日期对应的 UTC 零点（锚点）。
    /// 用于按天统计时的基准点，消除了直接比较 Local Time 或 UTC Date 带来的时区漂移问题。
    /// 例如：北京时间 2026-05-28 00:00:00 -> 返回 UTC 2026-05-27 16:00:00Z
    /// </summary>
    /// <param name="clock">时钟实例</param>
    /// <returns>本地今日零点的 UTC 时间</returns>
    public static DateTime GetLocalMidnightInUtc(this IClock clock)
    {
        var nowUtc = clock.Now;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local);
        return TimeZoneInfo.ConvertTimeToUtc(nowLocal.Date, TimeZoneInfo.Local);
    }

    /// <summary>
    /// 获取当前时区相对于 UTC 的偏移小时数
    /// </summary>
    /// <param name="clock">时钟实例</param>
    /// <returns>偏移小时数</returns>
    public static double GetLocalUtcOffsetHours(this IClock clock)
    {
        return TimeZoneInfo.Local.GetUtcOffset(clock.Now).TotalHours;
    }
}
