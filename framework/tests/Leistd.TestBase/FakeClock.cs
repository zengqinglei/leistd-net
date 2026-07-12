using Leistd.Timing;

namespace Leistd.TestBase;

/// <summary>
/// 可控时钟：测试中设定固定的 <see cref="Now"/>，替代 <see cref="UtcClockProvider"/>。
/// 默认 UTC 类型；<see cref="Normalize"/> 将时间统一转为 <see cref="Kind"/> 指定的类型。
/// </summary>
public sealed class FakeClock : IClock
{
    /// <summary>固定的当前时间（可读写，便于测试推进时间）。</summary>
    public DateTime Now { get; set; }

    /// <summary>时间类型，默认 UTC。</summary>
    public DateTimeKind Kind { get; set; } = DateTimeKind.Utc;

    /// <param name="now">初始当前时间；缺省为 2026-01-01 00:00:00 UTC。</param>
    public FakeClock(DateTime? now = null)
    {
        Now = now ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>把入参标准化为 <see cref="Kind"/> 指定的时间类型。</summary>
    public DateTime Normalize(DateTime dateTime)
    {
        if (dateTime.Kind == Kind)
        {
            return dateTime;
        }

        return Kind switch
        {
            DateTimeKind.Utc => dateTime.ToUniversalTime(),
            DateTimeKind.Local => dateTime.ToLocalTime(),
            _ => DateTime.SpecifyKind(dateTime, Kind),
        };
    }
}
