using System.Diagnostics;
using Leistd.EventBus.Distributed.Transport;

namespace Leistd.EventBus.Distributed.Diagnostics;

internal static class DistributedEventTrace
{
    public static string? FromHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var parent = headers.FirstOrDefault(pair => string.Equals(pair.Key, DistributedEventHeaders.TraceParent, StringComparison.Ordinal)).Value;
        return ActivityContext.TryParse(parent, null, out var context) ? context.TraceId.ToHexString() : null;
    }

    public static string? FromKeyword(string value)
        => value.Length == 32 && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
}
