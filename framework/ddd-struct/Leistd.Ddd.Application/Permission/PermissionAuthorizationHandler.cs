using Microsoft.AspNetCore.Authorization;

namespace Leistd.Ddd.Application.Permission;

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
        var isGranted = await permissionChecker.IsGrantedAsync(requirement.PermissionName);

        if (isGranted)
        {
            context.Succeed(requirement);
        }
    }
}
