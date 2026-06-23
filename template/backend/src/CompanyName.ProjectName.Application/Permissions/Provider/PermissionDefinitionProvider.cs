using Leistd.Ddd.Application.Permission;

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
            displayName: "应用权限"
        );

        // 用户管理
        var usersPermission = appGroup.AddPermission(
            PermissionConstant.Users.Default,
            displayName: "用户管理"
        );
        usersPermission.AddChild(PermissionConstant.Users.Create, displayName: "创建用户");
        usersPermission.AddChild(PermissionConstant.Users.Update, displayName: "更新用户");
        usersPermission.AddChild(PermissionConstant.Users.Delete, displayName: "删除用户");
        usersPermission.AddChild(PermissionConstant.Users.ManageRoles, displayName: "管理用户角色");

        // 角色管理
        var rolesPermission = appGroup.AddPermission(
            PermissionConstant.Roles.Default,
            displayName: "角色管理"
        );
        rolesPermission.AddChild(PermissionConstant.Roles.Create, displayName: "创建角色");
        rolesPermission.AddChild(PermissionConstant.Roles.Update, displayName: "更新角色");
        rolesPermission.AddChild(PermissionConstant.Roles.Delete, displayName: "删除角色");
        rolesPermission.AddChild(PermissionConstant.Roles.ManagePermissions, displayName: "管理角色权限");
    }
}
