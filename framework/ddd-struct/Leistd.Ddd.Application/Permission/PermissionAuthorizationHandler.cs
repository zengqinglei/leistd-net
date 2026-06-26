using Microsoft.AspNetCore.Authorization;

namespace Leistd.Ddd.Application.Permission;

/// <summary>
/// 权限授权处理器：将 <see cref="PermissionRequirement"/> 委托给
/// <see cref="IPermissionChecker"/> 校验，基于当前请求主体（claims）判定。
/// </summary>
/// <remarks>
/// 角色等信息来自 <see cref="System.Security.Claims.ClaimsPrincipal"/>（登录时写入），
/// 细粒度授予由 <see cref="IPermissionChecker"/> 实现自行决定来源（如授予存储）。
/// </remarks>
public class PermissionAuthorizationHandler(IPermissionChecker permissionChecker)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var isGranted = await permissionChecker.IsGrantedAsync(
            context.User,
            requirement.PermissionName);

        if (isGranted)
        {
            context.Succeed(requirement);
        }
    }
}
