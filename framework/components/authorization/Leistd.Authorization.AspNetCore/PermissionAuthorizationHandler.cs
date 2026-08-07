using Microsoft.AspNetCore.Authorization;
using Leistd.Authorization;

namespace Leistd.Authorization.AspNetCore;

/// <summary>
/// 权限授权处理器：将 <see cref="PermissionRequirement"/> 委托给
/// <see cref="IPermissionChecker"/> 校验。
/// </summary>
/// <remarks>
/// 当前用户信息由安全组件的 <c>ICurrentUser</c> 统一提供。
/// </remarks>
public class PermissionAuthorizationHandler(IPermissionChecker permissionChecker)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // 单权限走单权限重载；多权限时任一满足即通过。
        var isGranted = requirement.PermissionNames.Count == 1
            ? await permissionChecker.IsGrantedAsync(requirement.PermissionNames[0])
            : (await permissionChecker.IsGrantedAsync([.. requirement.PermissionNames])).AnyGranted;

        if (isGranted)
        {
            context.Succeed(requirement);
        }
    }
}

