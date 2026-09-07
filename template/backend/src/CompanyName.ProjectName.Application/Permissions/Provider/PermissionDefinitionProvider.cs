using Leistd.Authorization;
using Leistd.MultiTenancy;
using Leistd.Authorization.Abstractions;
using Leistd.MultiTenancy.Abstractions;
#if (LocalIdentity)
#endif

namespace CompanyName.ProjectName.Application.Permissions.Provider;

/// <summary>
/// 权限定义提供器
/// </summary>
/// <remarks>
/// 权限管理界面与授予存储均由这里的定义驱动。组仅用于模块展示与批量操作；
/// 资源权限同时表示读取该资源，子权限表示可执行的动作。
/// 动作权限以资源权限为祖先，写入授予时会自动补齐该祖先。
/// </remarks>
public class PermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // displayName 存的是本地化键：权限定义在启动时一次性加载并缓存，无法感知每个请求的
        // culture；真正的翻译在 PermissionAppService 下发定义时按请求 culture 完成。

        // 身份与访问：人（用户、角色）与代表第三方进来的开放应用，都是"谁能进来"这件事。
        var identityGroup = context.GetOrAddGroup(
            PermissionConstant.Groups.Identity,
            displayName: "Permission:Group.Identity"
        );

        var usersPermission = identityGroup.AddPermission(
            PermissionConstant.Users.Default,
            displayName: "Permission:App.Users"
        );
        usersPermission.AddChild(PermissionConstant.Users.Create, displayName: "Permission:App.Users.Create");
        usersPermission.AddChild(PermissionConstant.Users.Update, displayName: "Permission:App.Users.Update");
        usersPermission.AddChild(PermissionConstant.Users.Delete, displayName: "Permission:App.Users.Delete");
        usersPermission.AddChild(PermissionConstant.Users.ManageRoles, displayName: "Permission:App.Users.ManageRoles");

        var rolesPermission = identityGroup.AddPermission(
            PermissionConstant.Roles.Default,
            displayName: "Permission:App.Roles"
        );
        rolesPermission.AddChild(PermissionConstant.Roles.Create, displayName: "Permission:App.Roles.Create");
        rolesPermission.AddChild(PermissionConstant.Roles.Update, displayName: "Permission:App.Roles.Update");
        rolesPermission.AddChild(PermissionConstant.Roles.Delete, displayName: "Permission:App.Roles.Delete");
        rolesPermission.AddChild(PermissionConstant.Roles.ManagePermissions, displayName: "Permission:App.Roles.ManagePermissions");

#if (OpenIddictServer)
        var openApplicationsPermission = identityGroup.AddPermission(
            PermissionConstant.OpenApplications.Default,
            displayName: "Permission:App.OpenApplications"
        );
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.Create, displayName: "Permission:App.OpenApplications.Create");
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.Update, displayName: "Permission:App.OpenApplications.Update");
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.Delete, displayName: "Permission:App.OpenApplications.Delete");
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.ResetSecret, displayName: "Permission:App.OpenApplications.ResetSecret");

#endif
        // 系统
        var systemGroup = context.GetOrAddGroup(
            PermissionConstant.Groups.System,
            displayName: "Permission:Group.System"
        );
        systemGroup.AddPermission(
            PermissionConstant.Permissions.Default,
            displayName: "Permission:App.Permissions"
        );
        systemGroup.AddPermission(
            PermissionConstant.Settings.Default,
            displayName: "Permission:App.Settings"
        );

#if (LocalIdentity)
        // 宿主侧专属：租户上下文内不可见、不可授予（子权限继承父级侧别）
        var tenantsPermission = systemGroup.AddPermission(
            PermissionConstant.Tenants.Default,
            displayName: "Permission:App.Tenants",
            side: MultiTenancySides.Host
        );
        tenantsPermission.AddChild(PermissionConstant.Tenants.Create, displayName: "Permission:App.Tenants.Create");
        tenantsPermission.AddChild(PermissionConstant.Tenants.Update, displayName: "Permission:App.Tenants.Update");
        tenantsPermission.AddChild(PermissionConstant.Tenants.Delete, displayName: "Permission:App.Tenants.Delete");
#endif
    }
}
