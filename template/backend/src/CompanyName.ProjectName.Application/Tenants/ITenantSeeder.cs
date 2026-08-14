#if (TenancyEnabled)
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

    /// <summary>
    /// 清除当前租户上下文内由种子写入的数据，用于创建失败后的补偿
    /// </summary>
    /// <remarks>
    /// 与 <see cref="SeedAsync"/> 同处一个实现：种子写了什么、补偿就清什么，
    /// 改动种子时补偿在同一屏内，不会漏。幂等——可对部分播种的租户安全调用。
    /// </remarks>
    Task PurgeAsync(CancellationToken cancellationToken = default);
}
#endif
