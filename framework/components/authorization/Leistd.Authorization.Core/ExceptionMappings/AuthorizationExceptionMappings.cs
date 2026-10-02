using System.Net;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Exceptions;
using Leistd.ExceptionHandling.Descriptors;
using Leistd.ExceptionHandling.Options;
using Microsoft.Extensions.Logging;

namespace Leistd.Authorization.ExceptionMappings;

// 授权组件的非默认 HTTP 错误语义。
// AddPermissionAuthorizationCore 已自动登记，宿主不需要调用。默认值同时列在组件文档里；
// 要改其中任何一条，在自己的 GlobalExceptionOptions 里用 MapCode 或
// MapException 覆盖，与调用顺序无关。
internal static class AuthorizationExceptionMappings
{
    // 登记授权组件的非默认状态映射。
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(PermissionErrorCodes.SubjectNotFound, (int)HttpStatusCode.NotFound);
        options.MapDefaultCode(PermissionErrorCodes.ConcurrencyConflict, (int)HttpStatusCode.Conflict);
        options.MapDefaultException<UnstableGrantSnapshotException>(_ => new ExceptionDescriptor(
            (int)HttpStatusCode.ServiceUnavailable,
            PermissionErrorCodes.SnapshotUnavailable,
            "Authorization data is temporarily unavailable. Please retry.",
            LogLevel: LogLevel.Error));
    }
}
