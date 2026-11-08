using Leistd.EventBus.Distributed.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Dispatching;

internal static class DistributedEventRetry
{
    public static TimeSpan Delay(int attempt, DistributedEventOptions options)
    {
        var milliseconds = Math.Min(options.RetryMaxDelay.TotalMilliseconds,
            options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(30, Math.Max(0, attempt - 1))));
        return TimeSpan.FromMilliseconds(Math.Min(options.RetryMaxDelay.TotalMilliseconds,
            milliseconds * (0.9 + Random.Shared.NextDouble() * 0.2)));
    }
    public static string Truncate(string value) => value.Length > 512 ? value[..512] : value;
}
