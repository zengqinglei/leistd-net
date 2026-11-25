using Leistd.EventBus.Distributed.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Dispatching;

internal static class DistributedEventRetry
{
    // 只在本进程提交后会立即发送或处理时推迟新行首次可取时间，避免兜底扫描与立即路径争抢；没有立即路径时新行立即可扫描。
    public static readonly TimeSpan ImmediateGrace = TimeSpan.FromMinutes(1);

    public static TimeSpan Delay(int attempt, DistributedEventOptions options)
    {
        var milliseconds = Math.Min(options.RetryMaxDelay.TotalMilliseconds,
            options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(30, Math.Max(0, attempt - 1))));
        return TimeSpan.FromMilliseconds(Math.Min(options.RetryMaxDelay.TotalMilliseconds,
            milliseconds * (0.9 + Random.Shared.NextDouble() * 0.2)));
    }
    public static string Truncate(string value) => value.Length > 512 ? value[..512] : value;
}
