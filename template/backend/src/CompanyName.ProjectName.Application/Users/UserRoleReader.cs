using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Application.Users;

/// <summary>
/// 读取用户已落库的角色名，供登录签发的角色声明、令牌主体与当前用户资料使用；
/// 以及新用户应得的默认角色，供注册、首次外部登录与管理员未指定角色的建用户共用。
/// </summary>
/// <remarks>
/// 纯读取的跨聚合投影（用户角色关联 → 角色名），不含规则，所以在应用层而不在领域服务里。
/// 工作单元内刚插入、尚未落库的关联行读不到：写路径直接用自己刚分配的角色名。
/// </remarks>
public sealed class UserRoleReader(
    IRepository<UserRole, Guid> userRoleRepository,
    IRepository<Role, Guid> roleRepository,
    ILogger<UserRoleReader> logger)
{
    /// <summary>取用户的角色名；没有角色时为空列表。</summary>
    public async Task<List<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userRoles = await userRoleRepository.GetListAsync(ur => ur.UserId == userId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

        if (roleIds.Count == 0)
        {
            return [];
        }

        var roles = await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken);
        return [.. roles.Select(r => r.Name)];
    }

    /// <summary>取新用户应得的默认角色；没有配置默认角色时为空列表。</summary>
    /// <remarks>
    /// 默认角色是角色聚合上的标记，读它属于跨聚合读取，所以在应用层：领域服务只负责把选定的角色写成关联。
    /// </remarks>
    public async Task<List<Role>> GetDefaultRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = (await roleRepository.GetListAsync(r => r.IsDefault, cancellationToken)).ToList();
        if (roles.Count == 0)
        {
            logger.LogWarning("No default roles are configured; the new user is assigned no role");
        }

        return roles;
    }
}
