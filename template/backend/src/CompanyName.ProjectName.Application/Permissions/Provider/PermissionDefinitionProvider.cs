using Leistd.Authorization;

namespace CompanyName.ProjectName.Application.Permissions.Provider;

/// <summary>
/// 权限定义提供器
/// </summary>
/// <remarks>
/// 权限管理界面完全由这里的定义驱动：新增一个权限只需在此声明，
/// 无需改动前端的权限树或后端的授予存储。
/// </remarks>
public class PermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // displayName 存的是本地化键：权限定义在启动时一次性加载并缓存，无法感知每个请求的
        // culture；真正的翻译在 PermissionAppService 下发定义时按请求 culture 完成。
        var appGroup = context.GetOrAddGroup(
            PermissionConstant.GroupName,
            displayName: "Permission:App"
        );

        // 用户管理
        var usersPermission = appGroup.AddPermission(
            PermissionConstant.Users.Default,
            displayName: "Permission:App.Users"
        );
        usersPermission.AddChild(PermissionConstant.Users.Create, displayName: "Permission:App.Users.Create");
        usersPermission.AddChild(PermissionConstant.Users.Update, displayName: "Permission:App.Users.Update");
        usersPermission.AddChild(PermissionConstant.Users.Delete, displayName: "Permission:App.Users.Delete");
        usersPermission.AddChild(PermissionConstant.Users.ManageRoles, displayName: "Permission:App.Users.ManageRoles");
        usersPermission.AddChild(PermissionConstant.Users.ManagePermissions, displayName: "Permission:App.Users.ManagePermissions");

        // 角色管理
        var rolesPermission = appGroup.AddPermission(
            PermissionConstant.Roles.Default,
            displayName: "Permission:App.Roles"
        );
        rolesPermission.AddChild(PermissionConstant.Roles.Create, displayName: "Permission:App.Roles.Create");
        rolesPermission.AddChild(PermissionConstant.Roles.Update, displayName: "Permission:App.Roles.Update");
        rolesPermission.AddChild(PermissionConstant.Roles.Delete, displayName: "Permission:App.Roles.Delete");
        rolesPermission.AddChild(PermissionConstant.Roles.ManagePermissions, displayName: "Permission:App.Roles.ManagePermissions");

        // 权限定义
        appGroup.AddPermission(
            PermissionConstant.Permissions.Default,
            displayName: "Permission:App.Permissions"
        );
    }
}
