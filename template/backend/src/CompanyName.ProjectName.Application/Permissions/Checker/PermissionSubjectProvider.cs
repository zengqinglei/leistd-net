using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;

namespace CompanyName.ProjectName.Application.Permissions.Checker;

/// <summary>
/// 将模板身份模型适配为权限检查主体。
/// </summary>
public class PermissionSubjectProvider(
    ICurrentUser currentUser,
    IRepository<User, Guid> userRepository,
    IRepository<UserRole, Guid> userRoleRepository) : IPermissionSubjectProvider
{
    public async Task<PermissionSubject?> GetCurrentSubjectAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id;
        if (!userId.HasValue)
            return null;

        var userIdValue = userId.Value;
        var user = await userRepository.GetByIdAsync(userIdValue, cancellationToken);

        // 登录时拒绝禁用与锁定账号，但已签发的 Cookie/Bearer 不会因此失效。
        // 主体解析每请求查库，这里是 RBAC 路径上的每请求失效保障——放行就等于"禁用用户"只挡新登录，
        // 已在线的会话照常调用受权限保护的接口，与界面承诺的语义不符。
        //
        // 它不是唯一一道：默认策略上的 ActiveUserRequirement 覆盖每一次新的 HTTP 请求与
        // 每一次新的 Hub 握手，包括"仅要求已认证"的端点。两处判据相同，各自守住各自那条路径。
        //
        // 检查必须在超管分支之前：被禁用的超管同样要立刻失去权限。
        if (user == null || !user.IsActive || user.IsLockedOut())
            return null;

        if (user.IsSuperAdmin)
        {
            return new PermissionSubject(
                userIdValue.ToString(),
                [],
                IsSuperAdmin: true);
        }

        var roleIds = (await userRoleRepository.GetListAsync(ur => ur.UserId == userIdValue, cancellationToken))
            .Select(ur => ur.RoleId.ToString())
            .ToArray();

        return new PermissionSubject(
            userIdValue.ToString(),
            roleIds,
            IsSuperAdmin: false);
    }
}
