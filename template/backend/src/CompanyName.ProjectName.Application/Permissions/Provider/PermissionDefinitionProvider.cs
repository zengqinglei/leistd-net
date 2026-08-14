using Leistd.Authorization;
#if (TenancyEnabled)
using Leistd.MultiTenancy;
#endif

namespace CompanyName.ProjectName.Application.Permissions.Provider;

/// <summary>
/// 权限定义提供器
/// </summary>
/// <remarks>
/// 权限管理界面完全由这里的定义驱动：新增一个权限只需在此声明，
/// 无需改动前端的权限树或后端的授予存储。
///
/// 三层结构，与 ABP 的权限定义一致：
/// <list type="bullet">
/// <item><b>组</b>——模块，只做展示与批量操作的容器，本身不是权限；</item>
/// <item><b>资源权限</b>——模块下的一类对象（用户、角色），显示名用名词；
/// 它同时就是该资源的读权限，勾上即"能看到这份列表"；</item>
/// <item><b>动作权限</b>——可在该资源上执行的操作，显示名只用动词。</item>
/// </list>
///
/// 动作以资源权限为前置：不能查看列表就谈不上在列表上新增，写入时据此补齐祖先。
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

#if (IncludeOpenIddict)
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

#if (TenancyEnabled)
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
