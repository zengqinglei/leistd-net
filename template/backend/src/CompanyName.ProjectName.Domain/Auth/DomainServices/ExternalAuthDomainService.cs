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
/// <remarks>
/// 依赖 <see cref="UserDomainService"/> 只为复用它的<b>变更行为</b>
/// （<c>AssignDefaultRolesToUserAsync</c>）：「首次外部登录分配默认角色」是领域策略，
/// 放到应用层编排会让这条规则漂出领域。读取一律不经它，由调用方自己取。
/// </remarks>
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
    /// <returns>
    /// 用户；以及<b>本次刚分配</b>的角色名，没有新分配时为 <see langword="null"/>。
    /// </returns>
    /// <remarks>
    /// 只回传"刚分配的那批"，不代调用方回查已有角色。首次外部登录会在同一边界内新建用户并
    /// 分配默认角色，那些行此时还没落库，回查得到的会是空集合——所以这一批必须由本方法带出去。
    /// 其余情况返回 <see langword="null"/>，调用方按自己既有的回落取角色
    /// （<c>SessionSignInService</c> 已按 <c>roleNames ?? 回查</c> 处理）：读取不属于领域服务，
    /// 在这里回查等于把同一条回落规则在两层各写一份。
    /// </remarks>
    public async Task<(User User, List<string>? AssignedRoleNames)> FindOrCreateUserAsync(
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
            return (existingUser, null);
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

        // 新建用户带出刚分配的角色名（关联行尚未落库，回查不到）；按邮箱关联到的既有用户回 null，
        // 由调用方按既有回落取角色。
        return (user, assignedRoleNames);
    }
}
#endif
