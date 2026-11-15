namespace CompanyName.ProjectName.Domain.Users.Constants;

public static class AdminConstant
{
    public const string RoleName = "Admin";

    /// <summary>租户初始化时创建的租户管理员用户名。</summary>
    /// <remarks>
    /// 播种与部署级管理员恢复共用此初始用户名；模拟登录按内置 Admin 角色选择用户，不依赖用户名。
    /// </remarks>
    public const string TenantAdminUsername = "admin";
}
