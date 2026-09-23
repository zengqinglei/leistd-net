using Leistd.Authorization;
using Leistd.MultiTenancy;
using Leistd.Authorization.Checking;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Management;
using Leistd.Authorization.Subjects;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
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
    // 常规动作的默认文案统一取这三个值，与页面按钮一致；中文等译文由契约测试要求同样统一
    // （新建 / 编辑 / 删除），避免各模块各写一套「新增 / 修改」。
    internal const string CreateText = "Create";
    internal const string EditText = "Edit";
    internal const string DeleteText = "Delete";

    public void Define(IPermissionDefinitionContext context)
    {
        // displayName 是默认文案（英文），不启用本地化时直接展示；译文按约定键
        // Permission:{权限名}、PermissionGroup:{分组名} 在请求阶段查找（资源类型见 Program 的 PermissionManagementOptions）。
        // 分组、顺序与前端导航一致：分组对应菜单分组，根权限对应菜单项，子权限对应页面操作。

        // 身份与访问：谁能进来——用户、角色与租户
        var identityGroup = context.GetOrAddGroup(PermissionConstant.Groups.Identity, displayName: "Identity & access");

        var usersPermission = identityGroup.AddPermission(
            PermissionConstant.Users.Default,
            MultiTenancySides.Both,
            displayName: "User Management"
        );
        usersPermission.AddChild(PermissionConstant.Users.Create, displayName: CreateText);
        usersPermission.AddChild(PermissionConstant.Users.Update, displayName: EditText);
        usersPermission.AddChild(PermissionConstant.Users.Delete, displayName: DeleteText);
        usersPermission.AddChild(PermissionConstant.Users.ManageRoles, displayName: "Assign roles");

        var rolesPermission = identityGroup.AddPermission(
            PermissionConstant.Roles.Default,
            MultiTenancySides.Both,
            displayName: "Role Management"
        );
        rolesPermission.AddChild(PermissionConstant.Roles.Create, displayName: CreateText);
        rolesPermission.AddChild(PermissionConstant.Roles.Update, displayName: EditText);
        rolesPermission.AddChild(PermissionConstant.Roles.Delete, displayName: DeleteText);
        rolesPermission.AddChild(PermissionConstant.Roles.ManagePermissions, displayName: "Configure permissions");

#if (LocalIdentity)
        // 宿主侧专属：租户上下文内不可见、不可授予（子权限继承父级侧别）
        var tenantsPermission = identityGroup.AddPermission(
            PermissionConstant.Tenants.Default,
            MultiTenancySides.Host,
            displayName: "Tenant Management"
        );
        tenantsPermission.AddChild(PermissionConstant.Tenants.Create, displayName: CreateText);
        tenantsPermission.AddChild(PermissionConstant.Tenants.Update, displayName: EditText);
        tenantsPermission.AddChild(PermissionConstant.Tenants.Delete, displayName: DeleteText);
        tenantsPermission.AddChild(PermissionConstant.Tenants.Impersonation, displayName: "Sign in as tenant");

#endif
#if (OpenIddictServer)
        // 开放应用是宿主全局资源：OpenIddict 的四张表都没有 TenantId，也就不是 IMultiTenant，
        // 全局租户过滤器对它们不生效；OpenIddictDbContext 还固定连宿主控制库、不跟随租户路由。
        // 因此侧别必须是 Host——省略它会落到默认的 Both，租户管理员将拿到这组权限，
        // 进而读写全系统的 OAuth 客户端（重置密钥即可让该客户端对所有租户失效）。
        var developerGroup = context.GetOrAddGroup(PermissionConstant.Groups.Developer, displayName: "Developer");
        var openApplicationsPermission = developerGroup.AddPermission(
            PermissionConstant.OpenApplications.Default,
            MultiTenancySides.Host,
            displayName: "Open Applications"
        );
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.Create, displayName: CreateText);
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.Update, displayName: EditText);
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.Delete, displayName: DeleteText);
        openApplicationsPermission.AddChild(PermissionConstant.OpenApplications.ResetSecret, displayName: "Reset client secret");

#endif
        // 审计：记录带 TenantId 并受全局查询过滤器分区，宿主与租户各看各的，因此侧别是 Both
        var auditGroup = context.GetOrAddGroup(PermissionConstant.Groups.Audit, displayName: "Audit");
        var operationRecordsPermission = auditGroup.AddPermission(
            PermissionConstant.OperationRecords.Default,
            MultiTenancySides.Both,
            displayName: "Operation records"
        );
        // 导出与查看分开：导出把审计数据整批带离系统，之后不再受可见性分层约束、也不再有访问记录。
        operationRecordsPermission.AddChild(PermissionConstant.OperationRecords.Export, displayName: "Export");

        // 系统
        var systemGroup = context.GetOrAddGroup(PermissionConstant.Groups.System, displayName: "System");
        systemGroup.AddPermission(
            PermissionConstant.Settings.Default,
            MultiTenancySides.Both,
            displayName: "System settings"
        );
    }
}
