namespace CompanyName.ProjectName.Application.Permissions.Provider;

/// <summary>
/// 权限定义
/// </summary>
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
        public const string ManageRoles = Default + ".ManageRoles";
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
}
