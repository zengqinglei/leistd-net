using Leistd.Authorization;

namespace CompanyName.ProjectName.Application.Permissions.Provider;

/// <summary>
/// 权限定义提供器
/// </summary>
public class PermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        var appGroup = context.GetOrAddGroup(
            PermissionConstant.GroupName,
            displayName: "Application Permissions"
        );

        // 用户管理
        var usersPermission = appGroup.AddPermission(
            PermissionConstant.Users.Default,
            displayName: "User Management"
        );
        usersPermission.AddChild(PermissionConstant.Users.Create, displayName: "Create User");
        usersPermission.AddChild(PermissionConstant.Users.Update, displayName: "Update User");
        usersPermission.AddChild(PermissionConstant.Users.Delete, displayName: "Delete User");
        usersPermission.AddChild(PermissionConstant.Users.ManageRoles, displayName: "Manage User Roles");

        // 角色管理
        var rolesPermission = appGroup.AddPermission(
            PermissionConstant.Roles.Default,
            displayName: "Role Management"
        );
        rolesPermission.AddChild(PermissionConstant.Roles.Create, displayName: "Create Role");
        rolesPermission.AddChild(PermissionConstant.Roles.Update, displayName: "Update Role");
        rolesPermission.AddChild(PermissionConstant.Roles.Delete, displayName: "Delete Role");
        rolesPermission.AddChild(PermissionConstant.Roles.ManagePermissions, displayName: "Manage Role Permissions");
    }
}
