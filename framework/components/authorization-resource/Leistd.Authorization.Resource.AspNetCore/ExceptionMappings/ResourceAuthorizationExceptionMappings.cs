using Leistd.Authorization.Resource.Errors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace Leistd.Authorization.Resource.AspNetCore.ExceptionMappings;

/// <summary>资源授权组件的非默认 HTTP 错误语义，由宿主显式组合。</summary>
public static class ResourceAuthorizationExceptionMappings
{
    /// <summary>登记资源 ACL 写入的版本冲突状态。</summary>
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(ResourceAuthorizationErrorCodes.ConcurrencyConflict, StatusCodes.Status409Conflict);
    }
}
