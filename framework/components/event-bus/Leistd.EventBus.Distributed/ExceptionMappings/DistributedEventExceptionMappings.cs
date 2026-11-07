using System.Net;
using Leistd.EventBus.Distributed.Errors;
using Leistd.ExceptionHandling.Options;

namespace Leistd.EventBus.Distributed.ExceptionMappings;

internal static class DistributedEventExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(DistributedEventErrorCodes.RecordNotFound, (int)HttpStatusCode.NotFound);
        options.MapDefaultCode(DistributedEventErrorCodes.DatabaseNotFound, (int)HttpStatusCode.NotFound);
        options.MapDefaultCode(DistributedEventErrorCodes.HostOnly, (int)HttpStatusCode.Forbidden);
    }
}
