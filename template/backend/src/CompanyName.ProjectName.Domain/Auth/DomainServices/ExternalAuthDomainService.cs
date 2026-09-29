#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Errors;
using CompanyName.ProjectName.Domain.Users.Errors;
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
    /// <para>
    /// 按邮箱关联已有用户要求两边都已验证：提供商确认邮箱属于这个外部账号，且本地账号的邮箱也已确认。
    /// 否则任何人在提供商那里填上别人的邮箱（或抢先用别人的邮箱在本地注册）就能接管对方账号。
    /// 不满足时邮箱已被占用则拒绝，由用户先登录原账号、再在账号设置里绑定。
    /// 新建账号只采用已验证的邮箱（并记为已确认），未验证的换成占位地址。
    /// </para>
    /// </remarks>
    public async Task<(User User, List<string>? AssignedRoleNames)> FindOrCreateUserAsync(
        string provider,
        ExternalUserInfo externalUserInfo,
        CancellationToken cancellationToken = default)
    {
        // 先按外部连接查找，再用邮箱关联已有用户（两边都已验证才关联）。
        var connection = await externalLoginRepository.GetFirstAsync(
            c => c.Provider == provider && c.ProviderUserId == externalUserInfo.ProviderId,
            q => q.OrderBy(c => c.Id),
            cancellationToken);

        if (connection != null)
        {
            var existingUser = await userRepository.GetByIdAsync(connection.UserId, cancellationToken);
            if (existingUser == null)
            {
                throw new BusinessException(UserErrorCodes.NotFound, $"User {connection.UserId} not found.")
                    .WithData("Id", connection.UserId);
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
            if (user != null && !(externalUserInfo.EmailVerified && user.EmailConfirmed))
            {
                throw new BusinessException(
                        ExternalAuthErrorCodes.AccountExistsSignInToLink,
                        $"An account with this email already exists. Sign in to it and link {provider} from account settings.")
                    .WithData("Provider", provider);
            }
        }

        if (user == null)
        {
            // 未验证的邮箱不写进账号：写进去就占用了别人的地址，本人随后注册、登录都会被挡，
            // 找回时接手的还是挂着对方外部绑定的账号。原始邮箱只留在外部连接上
            var emailVerified = externalUserInfo.EmailVerified && !string.IsNullOrEmpty(externalUserInfo.Email);
            user = new User(
                username: externalUserInfo.Username,
                email: emailVerified ? externalUserInfo.Email! : $"{externalUserInfo.Username}@{provider.ToLower()}.local",
                passwordHash: null,
                displayName: externalUserInfo.DisplayName ?? externalUserInfo.Username
            );
            if (emailVerified)
            {
                // 提供商已确认：本人再用别的提供商（同一已验证邮箱）登录时能关联回来
                user.ConfirmEmail();
            }

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

    /// <summary>
    /// 把一个外部身份绑定到已登录的用户（"绑定"模式，而不是登录或建号）。
    /// </summary>
    /// <remarks>
    /// 一个外部身份只能属于一个用户：已绑在别人名下时拒绝，而不是改绑——
    /// 改绑等于让任何能登录这个外部账号的人把它从原主人那里抢走。
    /// 同一提供商每人只绑一个，界面按提供商展示，多个同类绑定说不清哪个在用。
    /// </remarks>
    public async Task<ExternalLoginConnection> LinkAsync(
        User user,
        string provider,
        ExternalUserInfo externalUserInfo,
        CancellationToken cancellationToken = default)
    {
        var existing = await externalLoginRepository.GetFirstAsync(
            c => c.Provider == provider && c.ProviderUserId == externalUserInfo.ProviderId,
            q => q.OrderBy(c => c.Id),
            cancellationToken);
        if (existing is not null && existing.UserId != user.Id)
        {
            throw new BusinessException(ExternalAuthErrorCodes.AlreadyLinked, $"This {provider} account is already linked to another user.")
                .WithData("Provider", provider);
        }

        if (existing is not null)
        {
            existing.Update(clock.Now, externalUserInfo.Username, externalUserInfo.Email, externalUserInfo.AvatarUrl);
            await externalLoginRepository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        if (await externalLoginRepository.AnyAsync(c => c.UserId == user.Id && c.Provider == provider, cancellationToken))
        {
            throw new BusinessException(ExternalAuthErrorCodes.ProviderAlreadyLinked, $"A {provider} account is already linked. Unlink it first.")
                .WithData("Provider", provider);
        }

        var connection = new ExternalLoginConnection(
            userId: user.Id,
            provider: provider,
            providerUserId: externalUserInfo.ProviderId,
            syncedAt: clock.Now,
            providerUsername: externalUserInfo.Username,
            providerEmail: externalUserInfo.Email,
            providerAvatarUrl: externalUserInfo.AvatarUrl);
        await externalLoginRepository.InsertAsync(connection, cancellationToken);

        logger.LogInformation("User {Username} linked a {Provider} login connection", user.Username, provider);
        return connection;
    }

    /// <summary>
    /// 解绑用户的一个外部身份。
    /// </summary>
    /// <remarks>
    /// 必须还剩一种登录方式（设有密码，或还有别的绑定），否则解绑之后这个账号就再也登不进来了。
    /// </remarks>
    /// <returns>被解绑的连接；不存在或不属于该用户时为 null。</returns>
    public async Task<ExternalLoginConnection?> UnlinkAsync(
        User user,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await externalLoginRepository.GetByIdAsync(connectionId, cancellationToken);
        if (connection is null || connection.UserId != user.Id)
            return null;

        var otherLinks = await externalLoginRepository.CountAsync(
            c => c.UserId == user.Id && c.Id != connectionId,
            cancellationToken);
        if (user.PasswordHash is null && otherLinks == 0)
        {
            throw new BusinessException(ExternalAuthErrorCodes.LastSignInMethod, "This is your only way to sign in. Set a password or link another account first.")
                ;
        }

        await externalLoginRepository.DeleteAsync(connection, cancellationToken);
        // 凭据变了：此前用这个外部账号完成第一步、尚待第二步的登录挑战随之作废
        user.RotateSecurityStamp();
        logger.LogInformation("User {Username} unlinked a {Provider} login connection", user.Username, connection.Provider);
        return connection;
    }
}
#endif
