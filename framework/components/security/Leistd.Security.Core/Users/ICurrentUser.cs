using System.Security.Claims;

namespace Leistd.Security.Users;

/// <summary>
/// 提供当前认证用户的信息。
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// 获取当前用户是否已认证。
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 获取用户标识符。
    /// </summary>
    /// <remarks>
    /// <b>返回 <see langword="null"/> 不等于未认证</b>：机器主体（client credentials）的
    /// <c>sub</c> 按 <see cref="Claims.ClientSubject"/> 契约形如 <c>client:&lt;client_id&gt;</c>，
    /// 不是 GUID，因此这里为 <see langword="null"/> 而 <see cref="IsAuthenticated"/> 仍为
    /// <see langword="true"/>。这是区分工作负载与自然人的判据，不是缺陷——审计的创建者字段
    /// 因此对机器写入留空，也是对的。
    /// </remarks>
    Guid? Id { get; }

    /// <summary>
    /// 所属租户 Id（来自 tenant_id claim；宿主用户为 null）
    /// </summary>
    /// <remarks>
    /// 这是主体 claim 的直读值，仅反映"签发 token 时用户属于哪个租户"；
    /// 运行时权威的租户上下文是 <c>Leistd.MultiTenancy</c> 的 <c>ICurrentTenant</c>。
    /// </remarks>
    Guid? TenantId { get; }

    /// <summary>
    /// 获取用户名。
    /// </summary>
    string? Username { get; }

    /// <summary>
    /// 获取用户显示名称。
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// 获取电子邮箱。
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// 获取电话号码。
    /// </summary>
    string? PhoneNumber { get; }

    /// <summary>
    /// 获取当前用户的所有角色。
    /// </summary>
    /// <returns>角色名称数组</returns>
    string[] GetRoles();

    /// <summary>
    /// 检查当前用户是否属于指定角色。
    /// </summary>
    /// <param name="roleName">角色名称（不区分大小写）</param>
    /// <returns>如果用户在该角色中则返回 true</returns>
    bool IsInRole(string roleName);

    /// <summary>
    /// 查找指定类型的第一个声明。
    /// </summary>
    /// <param name="claimType">Claim 类型</param>
    /// <returns>找到的 Claim，如果不存在则返回 null</returns>
    Claim? FindClaim(string claimType);

    /// <summary>
    /// 查找指定类型的所有声明。
    /// </summary>
    /// <param name="claimType">Claim 类型</param>
    /// <returns>找到的 Claims 数组</returns>
    Claim[] FindClaims(string claimType);

    /// <summary>
    /// 获取当前用户的所有声明。
    /// </summary>
    /// <returns>所有 Claims 数组</returns>
    Claim[] GetAllClaims();
}
