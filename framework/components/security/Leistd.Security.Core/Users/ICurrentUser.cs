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
    /// <see langword="null"/> 不代表未认证：机器主体的 <c>sub</c> 不是 GUID，
    /// 其 <see cref="IsAuthenticated"/> 仍可为 <see langword="true"/>；机器写入的用户审计标识为空。
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
