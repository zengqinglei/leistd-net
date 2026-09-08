#if (LocalIdentity)
using Leistd.Timing;
using Leistd.ExceptionHandling;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Domain.Auth.DomainServices;

/// <summary>
/// 外部认证领域服务
/// </summary>
public class ExternalAuthDomainService(
    IRepository<User, Guid> userRepository,
    IRepository<ExternalLoginConnection, Guid> externalLoginRepository,
    UserDomainService userDomainService,
    IClock clock,
    ILogger<ExternalAuthDomainService> logger)
{
    /// <summary>
    /// 查找或创建外部登录用户
    /// </summary>
    /// <returns>用户，以及其角色名称。</returns>
    /// <remarks>
    /// 角色名一并回传，调用方无需再查。首次外部登录会在同一边界内新建用户并分配默认角色，
    /// 那些行此时还没落库，回查得到的会是空集合——签发出的主体就少了全部角色。
    /// </remarks>
    public async Task<(User User, List<string> RoleNames)> FindOrCreateUserAsync(
        string provider,
        ExternalUserInfo externalUserInfo,
        CancellationToken cancellationToken = default)
    {
        // 先按外部连接查找，再用邮箱关联已有用户。
        var connection = await externalLoginRepository.GetFirstAsync(
            c => c.Provider == provider && c.ProviderUserId == externalUserInfo.ProviderId,
            q => q.OrderBy(c => c.Id),
            cancellationToken);

        if (connection != null)
        {
            var existingUser = await userRepository.GetByIdAsync(connection.UserId, cancellationToken);
            if (existingUser == null)
            {
                throw new NotFoundException($"User {connection.UserId} not found.")
#if (IncludeLocalization)
                    .WithCode("User:NotFound")
                    .WithData("Id", connection.UserId)
#endif
                    ;
            }

            logger.LogInformation("User {Username} signed in via {Provider}", existingUser.Username, provider);
            return (existingUser, await userDomainService.GetUserRoleNamesAsync(existingUser.Id, cancellationToken));
        }

        List<string>? assignedRoleNames = null;
        User? user = null;
        if (!string.IsNullOrEmpty(externalUserInfo.Email))
        {
            user = await userRepository.GetFirstAsync(
                u => u.Email == externalUserInfo.Email,
                q => q.OrderBy(u => u.Id),
                cancellationToken);
        }

        if (user == null)
        {
            user = new User(
                username: externalUserInfo.Username,
                email: externalUserInfo.Email ?? $"{externalUserInfo.Username}@{provider.ToLower()}.local",
                passwordHash: null,
                displayName: externalUserInfo.DisplayName ?? externalUserInfo.Username
            );

            if (!string.IsNullOrEmpty(externalUserInfo.AvatarUrl))
            {
                user.Update(user.DisplayName, user.PhoneNumber, externalUserInfo.AvatarUrl);
            }

            await userRepository.InsertAsync(user, cancellationToken);
            logger.LogInformation("Created a new user via {Provider}: {Username}", provider, user.Username);

            assignedRoleNames = await userDomainService.AssignDefaultRolesToUserAsync(user.Id, cancellationToken);
        }

        var newConnection = new ExternalLoginConnection(
            userId: user.Id,
            provider: provider,
            providerUserId: externalUserInfo.ProviderId,
            syncedAt: clock.Now,
            providerUsername: externalUserInfo.Username,
            providerEmail: externalUserInfo.Email,
            providerAvatarUrl: externalUserInfo.AvatarUrl
        );
        await externalLoginRepository.InsertAsync(newConnection, cancellationToken);

        logger.LogInformation("Linked user {Username} to a {Provider} login connection", user.Username, provider);

        // 新建用户用刚分配的角色名（关联行尚未落库，查不到）；按邮箱关联到的既有用户才回查
        return (user, assignedRoleNames
            ?? await userDomainService.GetUserRoleNamesAsync(user.Id, cancellationToken));
    }
}
#endif
