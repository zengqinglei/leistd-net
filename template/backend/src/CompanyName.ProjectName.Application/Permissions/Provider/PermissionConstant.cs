namespace CompanyName.ProjectName.Application.Permissions.Provider;

/// <summary>
/// 权限定义
/// </summary>
/// <remarks>
/// 全部权限统一使用 <c>App.</c> 前缀。这些常量既是权限定义的名称，也是
/// <c>[Authorize(Policy = ...)]</c> 的策略名和前端裁剪使用的契约，三处必须是同一组值。
/// </remarks>
public static class PermissionConstant
{
    /// <summary>
    /// 权限组前缀
    /// </summary>
    public const string GroupName = "App";

    /// <summary>
    /// 用户管理权限
    /// </summary>
    public static class Users
    {
        public const string Default = GroupName + ".Users";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";

        /// <summary>分配用户角色。与普通资料更新分离，避免持有编辑权限即可提权。</summary>
        public const string ManageRoles = Default + ".ManageRoles";

        /// <summary>为单个用户配置权限例外（直接授予或显式拒绝）。</summary>
        public const string ManagePermissions = Default + ".ManagePermissions";
    }

    /// <summary>
    /// 角色管理权限
    /// </summary>
    public static class Roles
    {
        public const string Default = GroupName + ".Roles";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string ManagePermissions = Default + ".ManagePermissions";
    }

    /// <summary>
    /// 权限定义查看权限
    /// </summary>
    public static class Permissions
    {
        /// <summary>查看权限定义树，是配置任何主体权限的前置条件。</summary>
        public const string Default = GroupName + ".Permissions";
    }
}
