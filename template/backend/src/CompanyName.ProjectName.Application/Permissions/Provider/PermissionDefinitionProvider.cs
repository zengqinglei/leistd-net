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
        // culture；真正的翻译在权限管理用例下发定义时按请求 culture 完成（资源类型见 Program 的 PermissionManagementOptions）。

        // 身份与访问：人（用户、角色）与代表第三方进来的开放应用，都是"谁能进来"这件事。
        var identityGroup = context.GetOrAddGroup(
            PermissionConstant.Groups.Identity,
            displayName: "Permission:Group.Identity"
        );

        var usersPermission = identityGroup.AddPermission(
            PermissionConstant.Users.Default,
            MultiTenancySides.Both,
            displayName: "Permission:App.Users"
        );
        usersPermission.AddChild(PermissionConstant.Users.Create, displayName: "Permission:App.Users.Create");
        usersPermission.AddChild(PermissionConstant.Users.Update, displayName: "Permission:App.Users.Update");
        usersPermission.AddChild(PermissionConstant.Users.Delete, displayName: "Permission:App.Users.Delete");
        usersPermission.AddChild(PermissionConstant.Users.ManageRoles, displayName: "Permission:App.Users.ManageRoles");

        var rolesPermission = identityGroup.AddPermission(
            PermissionConstant.Roles.Default,
            MultiTenancySides.Both,
            displayName: "Permission:App.Roles"
        );
        rolesPermission.AddChild(PermissionConstant.Roles.Create, displayName: "Permission:App.Roles.Create");
        rolesPermission.AddChild(PermissionConstant.Roles.Update, displayName: "Permission:App.Roles.Update");
        rolesPermission.AddChild(PermissionConstant.Roles.Delete, displayName: "Permission:App.Roles.Delete");
        rolesPermission.AddChild(PermissionConstant.Roles.ManagePermissions, displayName: "Permission:App.Roles.ManagePermissions");

#if (OpenIddictServer)
        // 开放应用是宿主全局资源：OpenIddict 的四张表都没有 TenantId，也就不是 IMultiTenant，
        // 全局租户过滤器对它们不生效；OpenIddictDbContext 还固定连宿主控制库、不跟随租户路由。
        // 因此侧别必须是 Host——省略它会落到默认的 Both，租户管理员将拿到这组权限，
        // 进而读写全系统的 OAuth 客户端（重置密钥即可让该客户端对所有租户失效）。
        var openApplicationsPermission = identityGroup.AddPermission(
            PermissionConstant.OpenApplications.Default,
            MultiTenancySides.Host,
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
            MultiTenancySides.Both,
            displayName: "Permission:App.Permissions"
        );
        systemGroup.AddPermission(
            PermissionConstant.Settings.Default,
            MultiTenancySides.Both,
            displayName: "Permission:App.Settings"
        );
        // 记录带 TenantId 并受全局查询过滤器分区，宿主与租户各看各的，因此侧别是 Both
        var operationRecordsPermission = systemGroup.AddPermission(
            PermissionConstant.OperationRecords.Default,
            MultiTenancySides.Both,
            displayName: "Permission:App.OperationRecords"
        );
        // 导出与查看分开：导出把审计数据整批带离系统，之后不再受可见性分层约束、也不再有访问记录。
        operationRecordsPermission.AddChild(
            PermissionConstant.OperationRecords.Export,
            displayName: "Permission:App.OperationRecords.Export"
        );

#if (LocalIdentity)
        // 宿主侧专属：租户上下文内不可见、不可授予（子权限继承父级侧别）
        var tenantsPermission = systemGroup.AddPermission(
            PermissionConstant.Tenants.Default,
            MultiTenancySides.Host,
            displayName: "Permission:App.Tenants"
        );
        tenantsPermission.AddChild(PermissionConstant.Tenants.Create, displayName: "Permission:App.Tenants.Create");
        tenantsPermission.AddChild(PermissionConstant.Tenants.Update, displayName: "Permission:App.Tenants.Update");
        tenantsPermission.AddChild(PermissionConstant.Tenants.Delete, displayName: "Permission:App.Tenants.Delete");
        tenantsPermission.AddChild(PermissionConstant.Tenants.Impersonation, displayName: "Permission:App.Tenants.Impersonation");
#endif
    }
}
