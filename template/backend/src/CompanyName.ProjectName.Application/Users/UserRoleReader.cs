using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;

namespace CompanyName.ProjectName.Application.Users;

/// <summary>
/// 读取用户已落库的角色名，供登录签发的角色声明、令牌主体与当前用户资料使用。
/// </summary>
/// <remarks>
/// 纯读取的跨聚合投影（用户角色关联 → 角色名），不含规则，所以在应用层而不在领域服务里。
/// 工作单元内刚插入、尚未落库的关联行读不到：写路径直接用自己刚分配的角色名。
/// </remarks>
public sealed class UserRoleReader(
    IRepository<UserRole, Guid> userRoleRepository,
    IRepository<Role, Guid> roleRepository)
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
}
