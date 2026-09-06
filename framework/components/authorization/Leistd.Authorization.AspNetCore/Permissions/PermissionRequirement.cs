using Microsoft.AspNetCore.Authorization;
using Leistd.Authorization.Abstractions;

namespace Leistd.Authorization.AspNetCore.Permissions;

/// <summary>
/// 权限授权需求：表示访问某资源所需的权限。持有多个权限名时按"任一满足"处理。
/// </summary>
/// <remarks>
/// 由 <see cref="PermissionPolicyProvider"/> 动态构建，由 <see cref="PermissionAuthorizationHandler"/> 经 <see cref="IPermissionChecker"/> 校验。
/// 多权限表达"任一满足"，用于某能力被另一能力隐含的场景。
/// </remarks>
public class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// 用单个权限构造。
    /// </summary>
    public PermissionRequirement(string permissionName)
        : this([permissionName])
    {
    }

    /// <summary>
    /// 用一组权限构造，任一满足即通过。
    /// </summary>
    public PermissionRequirement(IReadOnlyList<string> permissionNames)
    {
        ArgumentNullException.ThrowIfNull(permissionNames);

        PermissionNames = permissionNames;
    }

    /// <summary>
    /// 所需权限名称集合（与权限定义中的名称一致，如 "App.Users.Create"）。任一满足即通过。
    /// </summary>
    public IReadOnlyList<string> PermissionNames { get; }
}
