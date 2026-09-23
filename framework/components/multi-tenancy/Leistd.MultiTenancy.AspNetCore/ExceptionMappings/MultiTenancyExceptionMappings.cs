using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.MultiTenancy.Errors;
using Microsoft.AspNetCore.Http;

namespace Leistd.MultiTenancy.AspNetCore.ExceptionMappings;

/// <summary>多租户组件的非默认 HTTP 错误语义，由宿主显式组合。</summary>
public static class MultiTenancyExceptionMappings
{
    /// <summary>登记多租户组件的非默认状态映射。</summary>
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(MultiTenancyErrorCodes.NotActive, StatusCodes.Status403Forbidden);
        options.MapDefaultCode(MultiTenancyErrorCodes.NotFound, StatusCodes.Status404NotFound);
        options.MapDefaultCode(MultiTenancyErrorCodes.ConcurrencyConflict, StatusCodes.Status409Conflict);
        options.MapDefaultCode(MultiTenancyErrorCodes.DuplicateName, StatusCodes.Status409Conflict);
        options.MapDefaultCode(MultiTenancyErrorCodes.ConnectionChangeRequiresInactiveTenant, StatusCodes.Status409Conflict);
        options.MapDefaultCode(MultiTenancyErrorCodes.ConnectionVersionConflict, StatusCodes.Status409Conflict);
    }
}
