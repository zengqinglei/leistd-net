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
        // 主体解析每请求查库，是撤权唯一即时生效的地方——这里放行就等于"禁用用户"只挡新登录，
        // 已在线的会话照常调用全部受保护接口，与界面承诺的语义不符。
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
