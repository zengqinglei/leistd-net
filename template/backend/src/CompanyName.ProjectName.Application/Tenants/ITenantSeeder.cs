#if (IncludeTenancy)
namespace CompanyName.ProjectName.Application.Tenants;

/// <summary>
/// 租户初始化种子：在租户上下文内创建初始角色、权限授予与租户管理员
/// </summary>
/// <remarks>
/// 调用方必须先经 <c>ICurrentTenant.Change(tenantId)</c> 进入目标租户上下文——
/// 种子写入的所有行依赖多租户落值拦截器按环境上下文填充 TenantId。
/// </remarks>
public interface ITenantSeeder
{
    /// <summary>
    /// 在当前租户上下文内幂等地执行种子
    /// </summary>
    /// <param name="adminEmail">租户管理员邮箱</param>
    /// <param name="adminPassword">租户管理员初始密码（明文入参，仅存哈希）</param>
    Task SeedAsync(string adminEmail, string adminPassword, CancellationToken cancellationToken = default);
}
#endif
