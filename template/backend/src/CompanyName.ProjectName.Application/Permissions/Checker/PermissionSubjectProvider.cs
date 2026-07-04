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
        if (user == null)
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
