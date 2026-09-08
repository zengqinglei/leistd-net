namespace Leistd.Timing;

/// <summary>
/// 提供当前时间。
/// </summary>
/// <remarks>
/// 统一使用 UTC，不提供本地时间模式；按用户时区展示时由呈现层转换。
/// </remarks>
public interface IClock
{
    /// <summary>
    /// 获取当前 UTC 时间。
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// 将时间归一化为 UTC。
    /// </summary>
    /// <param name="dateTime">要归一化的时间。</param>
    /// <returns>归一化后的 UTC 时间。</returns>
    DateTime Normalize(DateTime dateTime);
}
