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
    /// 权限名前缀
    /// </summary>
    public const string Prefix = "App";

    /// <summary>
    /// 权限分组的标识符。
    /// </summary>
    /// <remarks>
    /// 分组是"模块"这一级，比资源粗一层：一个分组下放若干资源（用户、角色、开放应用），
    /// 每个资源再展开成动作。分组不是权限，因此不与任何 <c>App.*</c> 同名——
    /// 一旦组和某个权限共用标识符，同一个名字会在组标题和组内各出现一次，读起来像重复项。
    /// 分组名不落库（授予只存权限名），可自由调整。
    /// </remarks>
    public static class Groups
    {
        /// <summary>谁能进来、能做什么：用户、角色，以及代表第三方进来的开放应用。</summary>
        public const string Identity = "Group.Identity";

        public const string System = "Group.System";
    }

    /// <summary>
    /// 用户管理权限
    /// </summary>
    public static class Users
    {
        public const string Default = Prefix + ".Users";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";

        /// <summary>分配用户角色。与普通资料更新分离，避免持有编辑权限即可提权。</summary>
        public const string ManageRoles = Default + ".ManageRoles";
    }

    /// <summary>
    /// 角色管理权限
    /// </summary>
    public static class Roles
    {
        public const string Default = Prefix + ".Roles";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string ManagePermissions = Default + ".ManagePermissions";
    }

#if (OpenIddictServer)
    /// <summary>
    /// 开放应用（OAuth2 客户端）管理权限
    /// </summary>
    /// <remarks>
    /// 开放应用持有 ClientId/ClientSecret，能代表本系统对外颁发令牌，
    /// 因此与用户、角色同级独立成权限族，而不是复用用户管理权限。
    /// </remarks>
    public static class OpenApplications
    {
        public const string Default = Prefix + ".OpenApplications";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";

        /// <summary>重置客户端密钥。等价于换发凭据，与普通编辑分开授权。</summary>
        public const string ResetSecret = Default + ".ResetSecret";
    }

#endif
#if (LocalIdentity)
    /// <summary>
    /// 租户管理权限（宿主侧专属）
    /// </summary>
    /// <remarks>
    /// 定义时声明 Host 侧别：租户上下文内对任何主体（含租户管理员）不可见、不可授予、检查一律拒绝，
    /// 租户管理员不会在权限树里看到"管理租户"这类平台能力。
    /// </remarks>
    public static class Tenants
    {
        public const string Default = Prefix + ".Tenants";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }
#endif
    /// <summary>
    /// 设置管理权限
    /// </summary>
    /// <remarks>
    /// 只约束"改租户默认值"，因此是一个扁平权限而非"资源 + 动作"：查看设置不需要权限
    /// （个人偏好是个人数据），没有可作为资源层的读权限，硬造一个就没人检查。
    /// </remarks>
    public static class Settings
    {
        /// <summary>修改当前租户的设置默认值。</summary>
        public const string Default = Prefix + ".Settings";
    }

    /// <summary>
    /// 权限定义查看权限
    /// </summary>
    public static class Permissions
    {
        /// <summary>查看权限定义树，是配置任何主体权限的前置条件。</summary>
        public const string Default = Prefix + ".Permissions";

        /// <summary>
        /// 读取权限定义树的策略：本权限，或任一「配置主体权限」的权限。
        /// </summary>
        /// <remarks>
        /// 「能配置角色权限」必然蕴含「能读权限目录」。若要求管理员额外持有
        /// <see cref="Default"/>，就会存在一个永远无用的状态——有 ManagePermissions 却打不开
        /// 权限配置界面。用「任一满足」表达这层蕴含关系，而不是靠管理员记得多授一个根权限。
        /// </remarks>
        public const string ReadPolicy = Default + "|" + Roles.ManagePermissions;
    }
}
