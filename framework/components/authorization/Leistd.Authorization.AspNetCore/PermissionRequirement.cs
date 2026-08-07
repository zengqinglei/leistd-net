using Microsoft.AspNetCore.Authorization;

namespace Leistd.Authorization.AspNetCore;

/// <summary>
/// 权限授权需求：表示访问某资源所需的权限。持有多个权限名时按"任一满足"处理。
/// </summary>
/// <remarks>
/// 由 <see cref="PermissionPolicyProvider"/> 在解析 <c>[Authorize(Policy = "权限名")]</c>
/// 时动态构建，并由 <see cref="PermissionAuthorizationHandler"/> 通过
/// <see cref="IPermissionChecker"/> 校验。
///
/// 多权限用于表达"某能力被另一能力隐含"的场景——例如「读取权限定义树」是
/// 「配置角色权限」与「配置用户权限」的前置条件，把它做成一个可以被单独扣掉的权限，
/// 只会制造一个永远无用的状态。这类关系用"任一满足"表达，而不是要求管理员额外记得授一个根权限。
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
