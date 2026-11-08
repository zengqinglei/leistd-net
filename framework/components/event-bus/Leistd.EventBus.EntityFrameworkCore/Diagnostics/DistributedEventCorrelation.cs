using Leistd.EventBus.Distributed.Transport;
using Leistd.Tracing.Constants;

namespace Leistd.EventBus.EntityFrameworkCore.Diagnostics;

internal static class DistributedEventCorrelation
{
    public static string? FromHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var value = headers.FirstOrDefault(pair => string.Equals(pair.Key, DistributedEventHeaders.CorrelationId, StringComparison.Ordinal)).Value;
        return CorrelationIdConstants.IsWellFormed(value) ? value : null;
    }
}
