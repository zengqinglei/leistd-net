using Leistd.Authorization.Errors;
using Leistd.Authorization.Exceptions;
using Leistd.ExceptionHandling.AspNetCore.Descriptors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Leistd.Authorization.AspNetCore.ExceptionMappings;

/// <summary>授权组件的非默认 HTTP 错误语义，由宿主显式组合。</summary>
public static class AuthorizationExceptionMappings
{
    /// <summary>登记授权组件的非默认状态映射。</summary>
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(PermissionErrorCodes.SubjectNotFound, StatusCodes.Status404NotFound);
        options.MapDefaultCode(PermissionErrorCodes.ConcurrencyConflict, StatusCodes.Status409Conflict);
        options.MapDefaultException<UnstableGrantSnapshotException>(_ => new ExceptionDescriptor(
            StatusCodes.Status503ServiceUnavailable,
            PermissionErrorCodes.SnapshotUnavailable,
            "Authorization data is temporarily unavailable. Please retry.",
            LogLevel: LogLevel.Error));
    }
}
