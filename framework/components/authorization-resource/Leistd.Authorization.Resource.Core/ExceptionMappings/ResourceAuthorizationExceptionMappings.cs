using System.Net;
using Leistd.Authorization.Resource.Errors;
using Leistd.ExceptionHandling.Options;

namespace Leistd.Authorization.Resource.ExceptionMappings;

// 资源授权组件的非默认 HTTP 错误语义。
// AddResourceAuthorizationCore 已自动登记，宿主不需要调用。默认值同时列在组件文档里；
// 要改其中任何一条，在自己的 GlobalExceptionOptions 里用 MapCode 覆盖，与调用顺序无关。
internal static class ResourceAuthorizationExceptionMappings
{
    // 登记资源 ACL 写入的版本冲突状态。
    public static void Configure(GlobalExceptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapDefaultCode(ResourceAuthorizationErrorCodes.ConcurrencyConflict, (int)HttpStatusCode.Conflict);
    }
}
