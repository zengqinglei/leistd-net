using Microsoft.AspNetCore.Authorization;

namespace Leistd.Ddd.Application.Permission;

/// <summary>
/// 权限授权需求：表示访问某资源所需的单个权限。
/// </summary>
/// <remarks>
/// 由 <see cref="PermissionPolicyProvider"/> 在解析 <c>[Authorize(Policy = "权限名")]</c>
/// 时动态构建，并由 <see cref="PermissionAuthorizationHandler"/> 通过
/// <see cref="IPermissionChecker"/> 校验。
/// </remarks>
public class PermissionRequirement(string permissionName) : IAuthorizationRequirement
{
    /// <summary>
    /// 所需权限名称（与权限定义中的名称一致，如 "App.Users.Create"）。
    /// </summary>
    public string PermissionName { get; } = permissionName;
}
