using Leistd.Ddd.Domain.Entities.Auditing;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;

namespace CompanyName.ProjectName.Domain.Users.Entities;

/// <summary>
/// 用户角色关联实体
/// </summary>
/// <remarks>
/// 实现 <see cref="IMultiTenant"/>：本实体<b>被独立查询</b>（有自己的仓储，按 <c>UserId</c> /
/// <c>RoleId</c> 直接命中），因此必须自带租户维度，由全局过滤器与启动期检查接管。
/// <para><b>这是过渡形态，不是终局。</b>按 DDD，用户角色关联属于 User 聚合，本不该有独立仓储；
/// 终局是把它收进聚合、取消独立查询入口，那时租户维度由聚合根承担、本接口可以去掉。
/// 在那之前，"独立可查 ⇒ 自带租户维度"这条判据必须满足，否则隔离只是碰巧成立。
/// 判据见 docs/template/development-guide.md §8。</para>
/// </remarks>
public class UserRole : DeletionAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 所属租户 ID；<see langword="null"/> 表示宿主。
    /// </summary>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 用户 ID
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// 角色 ID
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// 导航属性 - 用户
    /// </summary>
    public User? User { get; private set; }

    /// <summary>
    /// 导航属性 - 角色
    /// </summary>
    public Role? Role { get; private set; }

    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        RoleId = roleId;
    }
}
