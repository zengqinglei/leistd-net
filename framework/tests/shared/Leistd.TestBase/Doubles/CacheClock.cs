using Microsoft.Extensions.Internal;

namespace Leistd.TestBase.Doubles;

/// <summary>将独立组件测试的可控时间源接入真实内存缓存。</summary>
/// <param name="timeProvider">测试使用的时间源。</param>
public sealed class CacheClock(TimeProvider timeProvider) : ISystemClock
{
    /// <summary>缓存用于计算到期的 UTC 时刻。</summary>
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
