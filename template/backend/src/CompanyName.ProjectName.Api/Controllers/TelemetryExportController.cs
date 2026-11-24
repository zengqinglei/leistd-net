using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>系统设置「运维」分组的业务动作：测试遥测导出。</summary>
/// <remarks>
/// 导出设置的读写走设置组件的通用端点；这里只放组件不认识的动作，路由与组件端点不重叠。
/// 放在 Api：它读的是宿主的导出选项（见 <see cref="TelemetryExportProbe"/>）。纯探测，不记操作记录。
/// </remarks>
[Authorize]
[Route("api/v1/settings/telemetry")]
public sealed class TelemetryExportController(TelemetryExportProbe probe) : BaseController
{
    /// <summary>用当前生效的导出地址与认证头发一次空的追踪导出（需要运维设置的管理权限）；导出开关关着也测。</summary>
    /// <remarks>不通过时以 422 返回原因码，见 <c>AppSettingErrorCodes.TelemetryExport*</c>。</remarks>
    [HttpPost("test")]
    [Authorize(Policy = PermissionConstant.Settings.Operations)]
    public Task TestAsync(CancellationToken cancellationToken = default)
        => probe.TestAsync(cancellationToken);
}
