using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.Authorization;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Subjects;
using Leistd.Timing;
using Leistd.Authorization.Checking;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Management;

namespace CompanyName.ProjectName.Application.Permissions.Provider;

/// <summary>
/// 将模板身份模型适配为权限检查主体。
/// </summary>
public class PermissionSubjectProvider(
    ICurrentUser currentUser,
    IRepository<User, Guid> userRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    IClock clock) : IPermissionSubjectProvider
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
        // 它不是唯一一道：经应用停用、删除账号时会话与令牌随之撤销，所有端点在认证阶段就拒绝；
        // 这里在 RBAC 路径上兜住绕过应用直接改库的情形，不等会话校验的缓存到期。
        //
        // 检查必须在超管分支之前：被禁用的超管同样要立刻失去权限。
        if (user == null || user.GetAccessStatus(clock.Now) != UserAccessStatus.Allowed)
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
